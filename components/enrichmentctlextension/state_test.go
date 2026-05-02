// Copyright Jaie Parker
// SPDX-License-Identifier: Apache-2.0

package enrichmentctlextension

import (
	"sync"
	"testing"
)

// BR-ENRICH-012 — concurrent reads see consistent snapshots via
// atomic-pointer swap to immutable maps. Run with `go test -race`.
func TestStore_ConcurrentReadWriteHasNoRace(t *testing.T) {
	s := NewStore()
	var wg sync.WaitGroup

	// 8 readers, each loops Load() and reads the maps.
	for r := 0; r < 8; r++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for i := 0; i < 1000; i++ {
				snap := s.Load()
				_ = snap.Persistent
				_ = snap.PerSession["session-1"]
				_ = snap.SessionEnabled("session-1")
			}
		}()
	}

	// 1 writer mutating concurrently.
	wg.Add(1)
	go func() {
		defer wg.Done()
		for i := 0; i < 200; i++ {
			s.Update(func(snap *Snapshot) {
				snap.Persistent["team"] = "platform"
				if snap.PerSession["session-1"] == nil {
					snap.PerSession["session-1"] = map[string]string{}
				}
				snap.PerSession["session-1"]["ticket.id"] = "PROJ-1234"
			})
		}
	}()

	wg.Wait()

	// Final state assertions.
	snap := s.Load()
	if snap.Persistent["team"] != "platform" {
		t.Fatalf("expected persistent team=platform, got %q", snap.Persistent["team"])
	}
	if snap.SessionAttrs("session-1")["ticket.id"] != "PROJ-1234" {
		t.Fatal("expected session ticket.id=PROJ-1234")
	}
}

// BR-ENRICH-006 — distinct sessions stay isolated.
func TestStore_PerSessionIsolation(t *testing.T) {
	s := NewStore()
	s.Update(func(snap *Snapshot) {
		snap.PerSession["A"] = map[string]string{"ticket.id": "JA-1"}
		snap.PerSession["B"] = map[string]string{"ticket.id": "JA-2"}
	})
	snap := s.Load()
	if snap.SessionAttrs("A")["ticket.id"] != "JA-1" {
		t.Fatal("session A leaked")
	}
	if snap.SessionAttrs("B")["ticket.id"] != "JA-2" {
		t.Fatal("session B leaked")
	}
}

// BR-ENRICH-008 — persistent and per-session both visible; the
// processor's stamping order tests are in enrichmentprocessor.
func TestSnapshot_BothMapsAccessible(t *testing.T) {
	s := NewStore()
	s.Update(func(snap *Snapshot) {
		snap.Persistent["team"] = "platform"
		snap.PerSession["A"] = map[string]string{"ticket.id": "JA-1"}
	})
	snap := s.Load()
	if snap.Persistent["team"] != "platform" || snap.SessionAttrs("A")["ticket.id"] != "JA-1" {
		t.Fatal("expected both maps populated")
	}
}

// BR-ENRICH-004 — collection-disabled flag flips correctly.
func TestSnapshot_CollectionDisabled(t *testing.T) {
	s := NewStore()
	if !s.Load().SessionEnabled("X") {
		t.Fatal("default should be enabled")
	}
	s.Update(func(snap *Snapshot) { snap.CollectionEnabled["X"] = false })
	if s.Load().SessionEnabled("X") {
		t.Fatal("expected disabled after Update")
	}
	s.Update(func(snap *Snapshot) { delete(snap.CollectionEnabled, "X") })
	if !s.Load().SessionEnabled("X") {
		t.Fatal("expected re-enabled after delete")
	}
}

// BR-ENRICH-001 / BR-ENRICH-002 — server-side validation helpers.
func TestValidKey(t *testing.T) {
	cases := map[string]bool{
		"team":            true,
		"ticket.id":       true,
		"feature_flag":    true,
		"a1.b2_c3-d4":     true,
		"":                false,
		"Team":            false,
		"1team":           false,
		"team!":           false,
		"team space":      false,
		"a" + string(make([]byte, 64)): false, // 65 chars — actually 65 with trailing zeros; expect false
	}
	for in, want := range cases {
		if got := validKey(in); got != want {
			t.Errorf("validKey(%q) = %v, want %v", in, got, want)
		}
	}
}

func TestValidValue(t *testing.T) {
	if !validValue("") {
		t.Error("empty value should be allowed")
	}
	if !validValue(string(make([]byte, 4096))) {
		t.Error("4096-byte value should be allowed")
	}
	if validValue(string(make([]byte, 4097))) {
		t.Error("4097-byte value should be rejected")
	}
}
