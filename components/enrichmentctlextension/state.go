// Copyright Jaie Parker
// SPDX-License-Identifier: Apache-2.0

package enrichmentctlextension

import (
	"sync/atomic"
)

// Snapshot is an immutable view of all enrichment state at one
// instant. Readers (the enrichmentprocessor) load via Store.Load()
// and read freely without locks.
//
// BR-ENRICH-012 — Snapshots are never mutated after construction;
// writers build a new Snapshot and atomically swap the pointer.
type Snapshot struct {
	// Persistent attributes apply to every record from every session.
	// Keyed by attribute key.
	Persistent map[string]string

	// PerSession enrichments. Outer key is session.id; inner is
	// attribute key → value.
	PerSession map[string]map[string]string

	// CollectionEnabled[sessionID] == false means the processor
	// drops batches whose session.id matches that key
	// (BR-ENRICH-004). Absence means enabled (default true).
	CollectionEnabled map[string]bool
}

// Store holds the atomic.Pointer to the current Snapshot.
//
// BR-ENRICH-012 implementation. Readers do a single atomic load
// per OTLP batch; writers do copy-on-write under a write mutex.
type Store struct {
	current atomic.Pointer[Snapshot]
}

// NewStore returns an empty Store.
func NewStore() *Store {
	s := &Store{}
	s.current.Store(&Snapshot{
		Persistent:        map[string]string{},
		PerSession:        map[string]map[string]string{},
		CollectionEnabled: map[string]bool{},
	})
	return s
}

// Load returns the current immutable Snapshot.
func (s *Store) Load() *Snapshot { return s.current.Load() }

// Update applies fn to a deep copy of the current Snapshot and
// atomically swaps the result into place. fn MUST NOT retain a
// reference to its argument.
func (s *Store) Update(fn func(*Snapshot)) {
	for {
		old := s.current.Load()
		next := copySnapshot(old)
		fn(next)
		if s.current.CompareAndSwap(old, next) {
			return
		}
		// Lost the race; retry. Rare with infrequent writers.
	}
}

func copySnapshot(s *Snapshot) *Snapshot {
	n := &Snapshot{
		Persistent:        make(map[string]string, len(s.Persistent)),
		PerSession:        make(map[string]map[string]string, len(s.PerSession)),
		CollectionEnabled: make(map[string]bool, len(s.CollectionEnabled)),
	}
	for k, v := range s.Persistent {
		n.Persistent[k] = v
	}
	for sid, m := range s.PerSession {
		n2 := make(map[string]string, len(m))
		for k, v := range m {
			n2[k] = v
		}
		n.PerSession[sid] = n2
	}
	for k, v := range s.CollectionEnabled {
		n.CollectionEnabled[k] = v
	}
	return n
}

// SessionAttrs returns the per-session map for sessionID, or nil
// if the session has none. Returned map MUST NOT be mutated.
func (snap *Snapshot) SessionAttrs(sessionID string) map[string]string {
	return snap.PerSession[sessionID]
}

// SessionEnabled returns true if the session's collection is
// enabled (default when no override is set).
func (snap *Snapshot) SessionEnabled(sessionID string) bool {
	v, ok := snap.CollectionEnabled[sessionID]
	if !ok {
		return true
	}
	return v
}
