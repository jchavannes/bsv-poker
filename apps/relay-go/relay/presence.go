// Package relay implements the Phase-1 hosted relay for bsv-poker.
//
// REQ-NET-001 (core §8.1): the relay is transport + indexing only and is
// NEVER the source of truth. It accelerates convergence and fans out opaque
// table messages; it never parses or adjudicates game logic. The truth is the
// validated transaction graph, reconstructed identically by each client (P2).
//
// This file: Tier A discovery — the in-memory player-presence registry with a
// heartbeat/expiry sweep (core §8.2 / REQ-NET-002; app §A7.2).
package relay

import (
	"errors"
	"sort"
	"sync"
	"time"
)

// Bounded-loop / Power-of-Ten discipline (core §A17): every sweep and every
// scan in this package iterates over a snapshot of a finite map, so loops are
// inherently bounded by the live registry size.

var (
	// ErrEmptyID rejects blank identifiers (defensive input validation).
	ErrEmptyID = errors.New("relay: empty id")
)

// Presence is a single player's discovery record (Tier A).
type Presence struct {
	PlayerID string `json:"playerId"`
	Addr     string `json:"addr"`     // opaque contact hint (relay does not interpret)
	LastSeen int64  `json:"lastSeen"` // unix nanoseconds of last heartbeat
}

// PresenceRegistry is an in-memory, heartbeat-expiring presence table.
// It is concurrency-safe. It holds no game state (REQ-NET-001).
type PresenceRegistry struct {
	mu      sync.Mutex
	ttl     time.Duration
	now     func() time.Time // injectable clock for deterministic tests
	players map[string]*Presence
}

// NewPresenceRegistry constructs a registry with the given heartbeat TTL.
// A non-positive ttl is replaced by a safe default so expiry never disables.
func NewPresenceRegistry(ttl time.Duration) *PresenceRegistry {
	if ttl <= 0 {
		ttl = 30 * time.Second
	}
	return &PresenceRegistry{
		ttl:     ttl,
		now:     time.Now,
		players: make(map[string]*Presence),
	}
}

// Heartbeat registers or refreshes a player's presence (the join/keepalive).
func (r *PresenceRegistry) Heartbeat(playerID, addr string) error {
	if playerID == "" {
		return ErrEmptyID
	}
	r.mu.Lock()
	defer r.mu.Unlock()
	p := r.players[playerID]
	if p == nil {
		p = &Presence{PlayerID: playerID}
		r.players[playerID] = p
	}
	p.Addr = addr
	p.LastSeen = r.now().UnixNano()
	return nil
}

// Remove drops a player immediately (explicit leave).
func (r *PresenceRegistry) Remove(playerID string) {
	r.mu.Lock()
	defer r.mu.Unlock()
	delete(r.players, playerID)
}

// Sweep evicts players whose last heartbeat is older than the TTL and returns
// the number removed. Callers run this on a bounded ticker.
func (r *PresenceRegistry) Sweep() int {
	r.mu.Lock()
	defer r.mu.Unlock()
	cutoff := r.now().Add(-r.ttl).UnixNano()
	removed := 0
	for id, p := range r.players { // bounded by len(players)
		if p.LastSeen < cutoff {
			delete(r.players, id)
			removed++
		}
	}
	return removed
}

// List returns a stable, alphabetically sorted snapshot of live presence.
func (r *PresenceRegistry) List() []Presence {
	r.mu.Lock()
	defer r.mu.Unlock()
	out := make([]Presence, 0, len(r.players))
	for _, p := range r.players { // bounded by len(players)
		out = append(out, *p)
	}
	sort.Slice(out, func(i, j int) bool { return out[i].PlayerID < out[j].PlayerID })
	return out
}

// Len reports the current number of live presence records.
func (r *PresenceRegistry) Len() int {
	r.mu.Lock()
	defer r.mu.Unlock()
	return len(r.players)
}
