// Tests for table-scoped capability tokens (audit finding 5): mint, verify, scope, expiry,
// table-scope isolation, tampering, admission gating, and HTTP fail-closed publish/subscribe.
package relay

import (
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"
)

func fixedMinter(t *testing.T) *capabilityMinter {
	t.Helper()
	m := newCapabilityMinter([]byte("test-secret-32-bytes-aaaaaaaaaaaa"))
	base := time.Unix(1_000_000, 0)
	m.now = func() time.Time { return base }
	return m
}

func TestCapabilityMintAndVerify(t *testing.T) {
	m := fixedMinter(t)
	tok, exp, err := m.mint("t1", ScopePubSub)
	if err != nil {
		t.Fatalf("mint: %v", err)
	}
	if exp <= m.now().Unix() {
		t.Fatalf("exp %d not in the future", exp)
	}
	if err := m.verify(tok, "t1", ScopePublish); err != nil {
		t.Fatalf("verify pub: %v", err)
	}
	if err := m.verify(tok, "t1", ScopeSubscribe); err != nil {
		t.Fatalf("verify sub: %v", err)
	}
}

func TestCapabilityRejectsMissing(t *testing.T) {
	m := fixedMinter(t)
	if err := m.verify("", "t1", ScopePublish); err != ErrNoCapability {
		t.Fatalf("empty token err = %v, want ErrNoCapability", err)
	}
}

func TestCapabilityRejectsWrongTable(t *testing.T) {
	m := fixedMinter(t)
	tok, _, _ := m.mint("t1", ScopePubSub)
	if err := m.verify(tok, "t2", ScopePublish); err != ErrBadCapability {
		t.Fatalf("cross-table err = %v, want ErrBadCapability", err)
	}
}

func TestCapabilityRejectsScopeEscalation(t *testing.T) {
	m := fixedMinter(t)
	subTok, _, _ := m.mint("t1", ScopeSubscribe)
	if err := m.verify(subTok, "t1", ScopePublish); err != ErrBadCapability {
		t.Fatalf("sub->pub escalation err = %v, want ErrBadCapability", err)
	}
	if err := m.verify(subTok, "t1", ScopeSubscribe); err != nil {
		t.Fatalf("sub verify: %v", err)
	}
}

func TestCapabilityRejectsExpired(t *testing.T) {
	m := fixedMinter(t)
	tok, exp, _ := m.mint("t1", ScopePubSub)
	m.now = func() time.Time { return time.Unix(exp+1, 0) }
	if err := m.verify(tok, "t1", ScopePublish); err != ErrBadCapability {
		t.Fatalf("expired err = %v, want ErrBadCapability", err)
	}
}

func TestCapabilityRejectsTampering(t *testing.T) {
	m := fixedMinter(t)
	tok, _, _ := m.mint("t1", ScopePubSub)
	// Flip a character in the payload and in the MAC; both must fail (forgery).
	for _, mut := range []string{tok[:5] + "X" + tok[6:], tok + "Z", "...", strings.ToUpper(tok)} {
		if err := m.verify(mut, "t1", ScopePublish); err == nil {
			t.Fatalf("tampered token %q verified, want rejection", mut)
		}
	}
}

func TestCapabilityRejectsForeignSecret(t *testing.T) {
	m1 := fixedMinter(t)
	m2 := newCapabilityMinter([]byte("a-completely-different-secret-key"))
	m2.now = m1.now
	tok, _, _ := m1.mint("t1", ScopePubSub)
	if err := m2.verify(tok, "t1", ScopePublish); err != ErrBadCapability {
		t.Fatalf("foreign-secret err = %v, want ErrBadCapability (rotation invalidates tokens)", err)
	}
}

func TestAdmissionGating(t *testing.T) {
	h := admissionHash("hunter2")
	if !admissionMatches(h, "hunter2") {
		t.Fatal("correct admission secret did not match")
	}
	if admissionMatches(h, "wrong") {
		t.Fatal("wrong admission secret matched")
	}
}

// ---- HTTP fail-closed enforcement ------------------------------------------

func newTestServer(t *testing.T) *Server {
	t.Helper()
	return NewServerWithSecret(time.Minute, []byte("http-test-secret-32-bytes-aaaaaa"))
}

func createTableHTTP(t *testing.T, s *Server, body string) createTableResp {
	t.Helper()
	rec := httptest.NewRecorder()
	req := httptest.NewRequest(http.MethodPost, "/tables", jsonBody(body))
	s.ServeHTTP(rec, req)
	if rec.Code != http.StatusCreated {
		t.Fatalf("create status = %d, want 201 (%s)", rec.Code, rec.Body.String())
	}
	var resp createTableResp
	mustJSON(t, rec.Body.Bytes(), &resp)
	if resp.Token == "" {
		t.Fatal("create did not return a capability token")
	}
	return resp
}

func TestPublishRequiresCapability(t *testing.T) {
	s := newTestServer(t)
	createTableHTTP(t, s, `{"id":"t1","name":"NL"}`)

	// No token → 401.
	rec := httptest.NewRecorder()
	req := httptest.NewRequest(http.MethodPost, "/tables/t1/publish", strings.NewReader("hello"))
	s.ServeHTTP(rec, req)
	if rec.Code != http.StatusUnauthorized {
		t.Fatalf("publish without token = %d, want 401", rec.Code)
	}

	// Forged token → 403.
	rec = httptest.NewRecorder()
	req = httptest.NewRequest(http.MethodPost, "/tables/t1/publish", strings.NewReader("hello"))
	req.Header.Set("Authorization", "Bearer not.a.valid.token")
	s.ServeHTTP(rec, req)
	if rec.Code != http.StatusForbidden {
		t.Fatalf("publish with forged token = %d, want 403", rec.Code)
	}
}

func TestSubscribeRequiresCapability(t *testing.T) {
	s := newTestServer(t)
	createTableHTTP(t, s, `{"id":"t1","name":"NL"}`)
	rec := httptest.NewRecorder()
	req := httptest.NewRequest(http.MethodGet, "/tables/t1/subscribe", nil)
	s.ServeHTTP(rec, req)
	if rec.Code != http.StatusUnauthorized {
		t.Fatalf("subscribe without token = %d, want 401", rec.Code)
	}
}

func TestPublishWithValidCapabilitySucceeds(t *testing.T) {
	s := newTestServer(t)
	resp := createTableHTTP(t, s, `{"id":"t1","name":"NL"}`)
	rec := httptest.NewRecorder()
	req := httptest.NewRequest(http.MethodPost, "/tables/t1/publish", strings.NewReader("hello"))
	req.Header.Set("Authorization", "Bearer "+resp.Token)
	s.ServeHTTP(rec, req)
	if rec.Code != http.StatusOK {
		t.Fatalf("publish with valid token = %d, want 200 (%s)", rec.Code, rec.Body.String())
	}
}

func TestMintCapabilityOpenTable(t *testing.T) {
	s := newTestServer(t)
	createTableHTTP(t, s, `{"id":"open1","name":"NL"}`)
	// Open table: anyone can mint (no admission), but a token is still required to use the channel.
	rec := httptest.NewRecorder()
	req := httptest.NewRequest(http.MethodPost, "/tables/open1/capability", jsonBody(`{}`))
	s.ServeHTTP(rec, req)
	if rec.Code != http.StatusOK {
		t.Fatalf("mint open = %d, want 200", rec.Code)
	}
	var resp mintCapabilityResp
	mustJSON(t, rec.Body.Bytes(), &resp)
	if err := s.caps.verify(resp.Token, "open1", ScopePublish); err != nil {
		t.Fatalf("minted open token failed verify: %v", err)
	}
}

func TestMintCapabilityGatedTable(t *testing.T) {
	s := newTestServer(t)
	createTableHTTP(t, s, `{"id":"vip","name":"NL","admission":"sekret"}`)

	// Wrong admission → 403.
	rec := httptest.NewRecorder()
	req := httptest.NewRequest(http.MethodPost, "/tables/vip/capability", jsonBody(`{"admission":"nope"}`))
	s.ServeHTTP(rec, req)
	if rec.Code != http.StatusForbidden {
		t.Fatalf("gated mint wrong secret = %d, want 403", rec.Code)
	}

	// Correct admission → 200 + usable token.
	rec = httptest.NewRecorder()
	req = httptest.NewRequest(http.MethodPost, "/tables/vip/capability", jsonBody(`{"admission":"sekret"}`))
	s.ServeHTTP(rec, req)
	if rec.Code != http.StatusOK {
		t.Fatalf("gated mint correct secret = %d, want 200 (%s)", rec.Code, rec.Body.String())
	}
	var resp mintCapabilityResp
	mustJSON(t, rec.Body.Bytes(), &resp)
	if err := s.caps.verify(resp.Token, "vip", ScopeSubscribe); err != nil {
		t.Fatalf("gated token failed verify: %v", err)
	}
}

// FuzzCapabilityVerify proves verify never panics on arbitrary token bytes (CWE-125/129/400).
func FuzzCapabilityVerify(f *testing.F) {
	m := fixedMinter(&testing.T{})
	good, _, _ := m.mint("t1", ScopePubSub)
	f.Add(good)
	f.Add("")
	f.Add("....")
	f.Add("a.b")
	for _, seed := range []string{"\x00\x01", strings.Repeat("A", 5000), "v1|t1|pubsub|9999999999"} {
		f.Add(seed)
	}
	f.Fuzz(func(t *testing.T, token string) {
		// Must return an error or nil, never panic, for any input and any table/scope.
		_ = m.verify(token, "t1", ScopePublish)
		_ = m.verify(token, "", ScopeSubscribe)
	})
}
