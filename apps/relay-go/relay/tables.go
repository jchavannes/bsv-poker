// Tier A table directory + Tier B table-scoped opaque fan-out.
//
// REQ-NET-002 (core §8.2): Tier A discovery (table create/join/list) and
// Tier B game-object propagation (per-table channels, Bitmessage-style). The
// relay STORES AND FORWARDS opaque bytes only; it does not parse, validate, or
// order game logic (REQ-NET-001, app §A7.3).
package relay

import (
	"errors"
	"sort"
	"sync"
)

var (
	// ErrTableExists is returned when creating a duplicate table id.
	ErrTableExists = errors.New("relay: table already exists")
	// ErrNoTable is returned for operations on an unknown table id.
	ErrNoTable = errors.New("relay: no such table")
)

// defaultFanoutBuffer bounds each subscriber's pending-message queue so a slow
// subscriber cannot grow memory without limit (Power-of-Ten bounded resource).
const defaultFanoutBuffer = 256

// Table is a Tier-A directory entry plus its Tier-B fan-out hub.
// The relay treats every published message as opaque bytes.
type Table struct {
	ID      string `json:"id"`
	Name    string `json:"name"`
	Members int    `json:"members"`

	// admissionHash is the hex SHA-256 of this table's admission secret, or "" for an OPEN table.
	// A gated table (non-empty hash) only mints a capability to a caller who presents the secret.
	// Never serialised — it never leaves the relay.
	admissionHash string

	mu   sync.Mutex
	seq  uint64
	subs map[uint64]chan []byte
}

func newTable(id, name, admissionHash string) *Table {
	return &Table{
		ID:            id,
		Name:          name,
		admissionHash: admissionHash,
		subs:          make(map[uint64]chan []byte),
	}
}

// Gated reports whether this table requires an admission secret to mint a capability.
func (t *Table) Gated() bool { return t.admissionHash != "" }

// AdmissionHash returns the stored admission hash ("" for an open table).
func (t *Table) AdmissionHash() string { return t.admissionHash }

// Subscribe registers a subscriber to the table's opaque object channel and
// returns the receive channel plus an unsubscribe func. Tier B (REQ-NET-002).
func (t *Table) Subscribe() (<-chan []byte, func()) {
	t.mu.Lock()
	defer t.mu.Unlock()
	id := t.seq
	t.seq++
	ch := make(chan []byte, defaultFanoutBuffer)
	t.subs[id] = ch
	t.Members = len(t.subs)
	unsub := func() {
		t.mu.Lock()
		defer t.mu.Unlock()
		if c, ok := t.subs[id]; ok {
			delete(t.subs, id)
			close(c)
			t.Members = len(t.subs)
		}
	}
	return ch, unsub
}

// Publish fans an opaque message out to every current subscriber.
// It returns the number of subscribers the message was delivered to. A copy is
// made per delivery so the caller's buffer is never aliased. If a subscriber's
// bounded buffer is full the message is dropped FOR THAT SUBSCRIBER ONLY (the
// relay is a best-effort speed path, never the source of truth — REQ-NET-001);
// the client reconciles via the canonical tx graph (REQ-NET-007).
func (t *Table) Publish(msg []byte) int {
	t.mu.Lock()
	defer t.mu.Unlock()
	delivered := 0
	for _, ch := range t.subs { // bounded by len(subs)
		cp := make([]byte, len(msg))
		copy(cp, msg)
		select {
		case ch <- cp:
			delivered++
		default:
			// subscriber backpressure: drop on speed path, do not block.
		}
	}
	return delivered
}

// SubscriberCount reports the live subscriber count for the table.
func (t *Table) SubscriberCount() int {
	t.mu.Lock()
	defer t.mu.Unlock()
	return len(t.subs)
}

// TableInfo is the JSON-serialisable directory view of a table.
type TableInfo struct {
	ID      string `json:"id"`
	Name    string `json:"name"`
	Members int    `json:"members"`
}

// TableRegistry is the concurrency-safe directory of tables (Tier A).
type TableRegistry struct {
	mu     sync.Mutex
	tables map[string]*Table
}

// NewTableRegistry constructs an empty table directory.
func NewTableRegistry() *TableRegistry {
	return &TableRegistry{tables: make(map[string]*Table)}
}

// Create registers a new OPEN table. Duplicate ids are rejected.
func (r *TableRegistry) Create(id, name string) (*Table, error) {
	return r.CreateGated(id, name, "")
}

// CreateGated registers a new table with an optional admission hash ("" = open). Duplicate ids are
// rejected. A gated table only mints capability tokens to callers who present the admission secret.
func (r *TableRegistry) CreateGated(id, name, admissionHash string) (*Table, error) {
	if id == "" {
		return nil, ErrEmptyID
	}
	r.mu.Lock()
	defer r.mu.Unlock()
	if _, ok := r.tables[id]; ok {
		return nil, ErrTableExists
	}
	t := newTable(id, name, admissionHash)
	r.tables[id] = t
	return t, nil
}

// Get returns a table by id, or ErrNoTable.
func (r *TableRegistry) Get(id string) (*Table, error) {
	r.mu.Lock()
	defer r.mu.Unlock()
	t, ok := r.tables[id]
	if !ok {
		return nil, ErrNoTable
	}
	return t, nil
}

// Join subscribes a client to an existing table's Tier-B channel. "Join" at the
// relay layer is exactly a subscription to the opaque fan-out (app §A7.3); seat
// occupancy is a game-state concept the relay never tracks (REQ-NET-001).
func (r *TableRegistry) Join(id string) (<-chan []byte, func(), error) {
	t, err := r.Get(id)
	if err != nil {
		return nil, nil, err
	}
	ch, unsub := t.Subscribe()
	return ch, unsub, nil
}

// List returns a stable, id-sorted snapshot of the table directory.
func (r *TableRegistry) List() []TableInfo {
	r.mu.Lock()
	defer r.mu.Unlock()
	out := make([]TableInfo, 0, len(r.tables))
	for _, t := range r.tables { // bounded by len(tables)
		out = append(out, TableInfo{ID: t.ID, Name: t.Name, Members: t.SubscriberCount()})
	}
	sort.Slice(out, func(i, j int) bool { return out[i].ID < out[j].ID })
	return out
}
