// Table-scoped capability tokens (audit finding 5): signed admission at the relay layer.
//
// Threat model (MS SDL): the relay is loopback transport, but a co-resident or networked process
// must not be able to (a) SUBSCRIBE to a table's channel and read its message stream, or
// (b) PUBLISH into a table's channel to poison/spam it, without first being ADMITTED. Envelope
// Ed25519 signatures (app layer) already stop ACTION FORGERY; capability tokens stop unauthorised
// channel access and bound abuse, and give us expiry + rotation + revocation.
//
// A capability is an HMAC-SHA256 over "v1|tableId|scope|exp", keyed by a per-process server secret.
// Properties:
//   - table-scoped: the tableId is inside the MAC, so a token for table A is invalid on table B;
//   - scoped: "pub", "sub" or "pubsub" — least privilege;
//   - expiring: exp (unix seconds) is inside the MAC and checked against the clock;
//   - unforgeable without the server secret (constant-time MAC compare);
//   - rotatable/revocable: rotating the server secret invalidates every outstanding token; rotating
//     a table's admission secret re-gates that table.
//
// Minting requires admission: a GATED table (one created with an admission secret) only mints a
// token to a caller who presents the matching secret (constant-time compare). Tokens are REQUIRED
// on publish/subscribe and the relay FAILS CLOSED (401) when one is missing or invalid.
package relay

import (
	"crypto/hmac"
	"crypto/rand"
	"crypto/sha256"
	"crypto/subtle"
	"encoding/base64"
	"errors"
	"strconv"
	"strings"
	"time"
)

// Capability scopes (least privilege).
const (
	ScopePublish   = "pub"
	ScopeSubscribe = "sub"
	ScopePubSub    = "pubsub"
)

// Default capability lifetime. A live session refreshes by re-minting before expiry.
const defaultCapabilityTTL = 6 * time.Hour

var (
	// ErrNoCapability is returned when a required capability token is absent.
	ErrNoCapability = errors.New("relay: capability token required")
	// ErrBadCapability is returned when a token is malformed, forged, expired, or mis-scoped.
	ErrBadCapability = errors.New("relay: invalid capability token")
	// ErrBadAdmission is returned when a gated table's admission secret does not match.
	ErrBadAdmission = errors.New("relay: admission denied")
)

// capabilityMinter mints and verifies capability tokens under a rotating server secret.
type capabilityMinter struct {
	secret []byte
	ttl    time.Duration
	now    func() time.Time // injectable clock for deterministic tests
}

// newCapabilityMinter builds a minter. If secret is empty a 32-byte CSPRNG secret is generated, so
// the relay is NEVER keyless: every token is bound to an unguessable per-process secret.
func newCapabilityMinter(secret []byte) *capabilityMinter {
	if len(secret) == 0 {
		secret = make([]byte, 32)
		if _, err := rand.Read(secret); err != nil {
			// CSPRNG failure is unrecoverable for a security boundary — fail closed at startup.
			panic("relay: cannot generate capability secret: " + err.Error())
		}
	}
	return &capabilityMinter{secret: secret, ttl: defaultCapabilityTTL, now: time.Now}
}

// validScope reports whether s is one of the recognised scope strings.
func validScope(s string) bool {
	return s == ScopePublish || s == ScopeSubscribe || s == ScopePubSub
}

// mint issues a token for (tableID, scope) expiring ttl from now. Inputs are validated; callers
// upstream have already authorised the mint (admission check / table creation).
func (m *capabilityMinter) mint(tableID, scope string) (string, int64, error) {
	if tableID == "" {
		return "", 0, ErrEmptyID
	}
	if !validScope(scope) {
		return "", 0, ErrBadCapability
	}
	exp := m.now().Add(m.ttl).Unix()
	payload := "v1|" + tableID + "|" + scope + "|" + strconv.FormatInt(exp, 10)
	mac := m.macHex(payload)
	tok := b64(payload) + "." + b64([]byte(mac))
	return tok, exp, nil
}

// verify checks a token against the wanted scope for tableID, returning nil on success. Every
// failure path returns ErrBadCapability/ErrNoCapability — never a partial trust. Constant-time MAC
// comparison (CWE-208/CWE-697); bounded parsing with no out-of-range indexing (CWE-125/129).
func (m *capabilityMinter) verify(token, tableID, wantScope string) error {
	if token == "" {
		return ErrNoCapability
	}
	if len(token) > 4096 { // attack-surface bound: nothing legitimate is this large
		return ErrBadCapability
	}
	dot := strings.IndexByte(token, '.')
	if dot <= 0 || dot >= len(token)-1 {
		return ErrBadCapability
	}
	payloadBytes, err1 := unb64(token[:dot])
	macBytes, err2 := unb64(token[dot+1:])
	if err1 != nil || err2 != nil {
		return ErrBadCapability
	}
	payload := string(payloadBytes)
	// Recompute the MAC and compare in constant time BEFORE trusting any field.
	expectMac := m.macHex(payload)
	if subtle.ConstantTimeCompare(macBytes, []byte(expectMac)) != 1 {
		return ErrBadCapability
	}
	// Parse "v1|tableId|scope|exp" — exactly four fields.
	parts := strings.Split(payload, "|")
	if len(parts) != 4 || parts[0] != "v1" {
		return ErrBadCapability
	}
	gotTable, gotScope, gotExp := parts[1], parts[2], parts[3]
	if subtle.ConstantTimeCompare([]byte(gotTable), []byte(tableID)) != 1 {
		return ErrBadCapability // table-scope mismatch
	}
	exp, err := strconv.ParseInt(gotExp, 10, 64)
	if err != nil {
		return ErrBadCapability
	}
	if m.now().Unix() >= exp {
		return ErrBadCapability // expired
	}
	if !scopeAllows(gotScope, wantScope) {
		return ErrBadCapability // insufficient scope
	}
	return nil
}

// scopeAllows reports whether a token carrying have-scope authorises a want-scope operation.
func scopeAllows(have, want string) bool {
	if !validScope(have) || !validScope(want) {
		return false
	}
	if have == ScopePubSub {
		return true
	}
	return have == want
}

func (m *capabilityMinter) macHex(payload string) string {
	h := hmac.New(sha256.New, m.secret)
	_, _ = h.Write([]byte(payload))
	return base64.RawURLEncoding.EncodeToString(h.Sum(nil))
}

func b64(s any) string {
	switch v := s.(type) {
	case string:
		return base64.RawURLEncoding.EncodeToString([]byte(v))
	case []byte:
		return base64.RawURLEncoding.EncodeToString(v)
	default:
		return ""
	}
}

func unb64(s string) ([]byte, error) {
	return base64.RawURLEncoding.DecodeString(s)
}

// admissionHash returns the hex SHA-256 of a table admission secret (what the table stores, never
// the secret itself).
func admissionHash(secret string) string {
	sum := sha256.Sum256([]byte(secret))
	return base64.RawURLEncoding.EncodeToString(sum[:])
}

// admissionMatches compares a presented admission secret to a stored hash in constant time.
func admissionMatches(storedHash, presented string) bool {
	got := admissionHash(presented)
	return subtle.ConstantTimeCompare([]byte(got), []byte(storedHash)) == 1
}
