// Copyright Jaie Parker
// SPDX-License-Identifier: Apache-2.0

package enrichmentctlextension

import (
	"context"
	"encoding/json"
	"errors"
	"net"
	"net/http"
	"os"
	"sync"
	"time"

	"go.opentelemetry.io/collector/component"
	"go.opentelemetry.io/collector/extension"
)

// Extension hosts the enrichment state and the HTTP control API.
type Extension struct {
	cfg     *Config
	store   *Store
	server  *http.Server
	persMu  sync.Mutex // serialises persistent-enrichments writes
	started bool
}

func newExtension(cfg *Config) *Extension {
	return &Extension{cfg: cfg, store: NewStore()}
}

// Store returns the in-process Store the processor reads from.
// Called from the processor's Start (BR-ENRICH-012 — readers see
// consistent snapshots via atomic.Pointer in the Store).
func (e *Extension) Store() *Store { return e.store }

// Start launches the HTTP control API and loads persistent-enrichments
// from disk if the file exists.
func (e *Extension) Start(ctx context.Context, host component.Host) error {
	if err := e.loadPersistent(); err != nil {
		return err
	}

	mux := http.NewServeMux()
	e.registerRoutes(mux)

	e.server = &http.Server{
		Addr:              e.cfg.Endpoint,
		Handler:           mux,
		ReadHeaderTimeout: 5 * time.Second,
	}

	ln, err := net.Listen("tcp", e.cfg.Endpoint)
	if err != nil {
		return err
	}

	go func() {
		_ = e.server.Serve(ln)
	}()

	e.started = true
	return nil
}

// Shutdown stops the HTTP server.
func (e *Extension) Shutdown(ctx context.Context) error {
	if !e.started || e.server == nil {
		return nil
	}
	return e.server.Shutdown(ctx)
}

// loadPersistent reads PersistentFile from disk into the Store.
// Missing file is fine (empty persistent map).
func (e *Extension) loadPersistent() error {
	data, err := os.ReadFile(e.cfg.PersistentFile)
	if err != nil {
		if errors.Is(err, os.ErrNotExist) {
			return nil // no persistent enrichments yet
		}
		return err
	}
	var m map[string]string
	if len(data) == 0 {
		return nil
	}
	if err := json.Unmarshal(data, &m); err != nil {
		return err
	}
	e.store.Update(func(s *Snapshot) {
		s.Persistent = m
	})
	return nil
}

// savePersistent writes the current persistent map to disk.
// BR-ENRICH-009 — file is the single source of truth; we always
// write through.
func (e *Extension) savePersistent() error {
	e.persMu.Lock()
	defer e.persMu.Unlock()

	snap := e.store.Load()
	data, err := json.MarshalIndent(snap.Persistent, "", "  ")
	if err != nil {
		return err
	}
	return os.WriteFile(e.cfg.PersistentFile, data, 0o644)
}

// ---------------------------------------------------------------
// HTTP routes
// ---------------------------------------------------------------

func (e *Extension) registerRoutes(mux *http.ServeMux) {
	// Per-session enrichments (BR-ENRICH-006 isolation, BR-ENRICH-001/002 validation server-side).
	mux.HandleFunc("GET /sessions/{id}/enrichments", e.handleGetSessionEnrichments)
	mux.HandleFunc("POST /sessions/{id}/enrichments", e.handlePostSessionEnrichments)

	// Per-session collection (BR-ENRICH-004 drop-on-disabled).
	mux.HandleFunc("GET /sessions/{id}/collection", e.handleGetCollection)
	mux.HandleFunc("POST /sessions/{id}/collection", e.handlePostCollection)

	// Persistent (BR-ENRICH-007/008/009/013/014).
	mux.HandleFunc("GET /persistent-enrichments", e.handleGetPersistent)
	mux.HandleFunc("POST /persistent-enrichments", e.handlePostPersistent)
	mux.HandleFunc("GET /persistent-enrichments/{key}", e.handleGetPersistentKey)

	// Restart no-op (the dispatcher honours it; a real restart is the user's call).
	mux.HandleFunc("POST /control/restart", e.handleRestart)
}

// --- per-session enrichments ---

func (e *Extension) handleGetSessionEnrichments(w http.ResponseWriter, r *http.Request) {
	id := r.PathValue("id")
	snap := e.store.Load()
	m := snap.SessionAttrs(id)
	if m == nil {
		m = map[string]string{}
	}
	writeJSON(w, http.StatusOK, m)
}

type sessionEnrichmentOp struct {
	Op    string `json:"op"`
	Key   string `json:"key,omitempty"`
	Value string `json:"value,omitempty"`
}

func (e *Extension) handlePostSessionEnrichments(w http.ResponseWriter, r *http.Request) {
	id := r.PathValue("id")
	var op sessionEnrichmentOp
	if err := json.NewDecoder(r.Body).Decode(&op); err != nil {
		writeError(w, http.StatusBadRequest, "invalid JSON: "+err.Error())
		return
	}
	switch op.Op {
	case "set":
		if !validKey(op.Key) {
			writeError(w, http.StatusBadRequest, "invalid key (BR-ENRICH-001)")
			return
		}
		if !validValue(op.Value) {
			writeError(w, http.StatusBadRequest, "invalid value (BR-ENRICH-002)")
			return
		}
		e.store.Update(func(s *Snapshot) {
			m, ok := s.PerSession[id]
			if !ok {
				m = map[string]string{}
				s.PerSession[id] = m
			}
			m[op.Key] = op.Value
		})
	case "remove":
		e.store.Update(func(s *Snapshot) {
			if m, ok := s.PerSession[id]; ok {
				delete(m, op.Key)
				if len(m) == 0 {
					delete(s.PerSession, id)
				}
			}
		})
	case "clear":
		e.store.Update(func(s *Snapshot) {
			delete(s.PerSession, id)
		})
	default:
		writeError(w, http.StatusBadRequest, "unknown op: "+op.Op)
		return
	}
	writeJSON(w, http.StatusOK, map[string]string{"ok": "true"})
}

// --- per-session collection toggle ---

func (e *Extension) handleGetCollection(w http.ResponseWriter, r *http.Request) {
	id := r.PathValue("id")
	snap := e.store.Load()
	state := "on"
	if !snap.SessionEnabled(id) {
		state = "off"
	}
	writeJSON(w, http.StatusOK, state)
}

type collectionOp struct {
	Enabled bool `json:"enabled"`
}

func (e *Extension) handlePostCollection(w http.ResponseWriter, r *http.Request) {
	id := r.PathValue("id")
	var op collectionOp
	if err := json.NewDecoder(r.Body).Decode(&op); err != nil {
		writeError(w, http.StatusBadRequest, "invalid JSON: "+err.Error())
		return
	}
	e.store.Update(func(s *Snapshot) {
		if op.Enabled {
			delete(s.CollectionEnabled, id) // default is enabled, save space
		} else {
			s.CollectionEnabled[id] = false
		}
	})
	writeJSON(w, http.StatusOK, map[string]string{"ok": "true"})
}

// --- persistent enrichments ---

type persistentEntry struct {
	Key    string `json:"key"`
	Value  any    `json:"value"`
	Exists bool   `json:"exists"`
}

func (e *Extension) handleGetPersistent(w http.ResponseWriter, r *http.Request) {
	keys := r.URL.Query()["keys"]
	snap := e.store.Load()

	if len(keys) > 0 {
		// BR-ENRICH-014 — multi-key form, always 200 with array.
		out := make([]persistentEntry, 0, len(keys))
		for _, k := range keys {
			v, ok := snap.Persistent[k]
			if ok {
				out = append(out, persistentEntry{Key: k, Value: v, Exists: true})
			} else {
				out = append(out, persistentEntry{Key: k, Value: nil, Exists: false})
			}
		}
		writeJSON(w, http.StatusOK, out)
		return
	}

	// no keys query → return whole map
	writeJSON(w, http.StatusOK, snap.Persistent)
}

func (e *Extension) handleGetPersistentKey(w http.ResponseWriter, r *http.Request) {
	// BR-ENRICH-013 — single value or 404.
	key := r.PathValue("key")
	snap := e.store.Load()
	v, ok := snap.Persistent[key]
	if !ok {
		w.WriteHeader(http.StatusNotFound)
		return
	}
	w.Header().Set("Content-Type", "text/plain; charset=utf-8")
	_, _ = w.Write([]byte(v))
}

func (e *Extension) handlePostPersistent(w http.ResponseWriter, r *http.Request) {
	var op sessionEnrichmentOp // same shape (op/key/value)
	if err := json.NewDecoder(r.Body).Decode(&op); err != nil {
		writeError(w, http.StatusBadRequest, "invalid JSON: "+err.Error())
		return
	}
	switch op.Op {
	case "set":
		if !validKey(op.Key) {
			writeError(w, http.StatusBadRequest, "invalid key (BR-ENRICH-001)")
			return
		}
		if !validValue(op.Value) {
			writeError(w, http.StatusBadRequest, "invalid value (BR-ENRICH-002)")
			return
		}
		e.store.Update(func(s *Snapshot) {
			s.Persistent[op.Key] = op.Value
		})
	case "remove":
		e.store.Update(func(s *Snapshot) {
			delete(s.Persistent, op.Key)
		})
	case "clear":
		// BR-ENRICH-011 — confirmation enforced at the SKILL/helper layer.
		e.store.Update(func(s *Snapshot) {
			s.Persistent = map[string]string{}
		})
	default:
		writeError(w, http.StatusBadRequest, "unknown op: "+op.Op)
		return
	}
	if err := e.savePersistent(); err != nil {
		writeError(w, http.StatusInternalServerError, "save failed: "+err.Error())
		return
	}
	writeJSON(w, http.StatusOK, map[string]string{"ok": "true"})
}

// --- restart ---

func (e *Extension) handleRestart(w http.ResponseWriter, r *http.Request) {
	// v1: no-op acknowledgement. The user runs the actual restart.
	writeJSON(w, http.StatusOK, map[string]string{"ok": "true", "note": "restart is a user action; this endpoint just acknowledges"})
}

// --- helpers ---

func writeJSON(w http.ResponseWriter, status int, body any) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(body)
}

func writeError(w http.ResponseWriter, status int, msg string) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(map[string]string{"error": msg})
}

// validKey enforces BR-ENRICH-001 — key matches ^[a-z][a-z0-9_.\-]*$ and is ≤ 64 chars.
func validKey(k string) bool {
	if len(k) == 0 || len(k) > 64 {
		return false
	}
	if !(k[0] >= 'a' && k[0] <= 'z') {
		return false
	}
	for i := 1; i < len(k); i++ {
		c := k[i]
		ok := (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') ||
			c == '_' || c == '.' || c == '-'
		if !ok {
			return false
		}
	}
	return true
}

// validValue enforces BR-ENRICH-002 — length ≤ 4096.
func validValue(v string) bool {
	return len(v) <= 4096
}

// Type and Stability are exported so other components can reference us.
var (
	Type      = component.MustNewType("enrichmentctl")
	Stability = component.StabilityLevelDevelopment
)

// NewFactory returns the extension factory.
func NewFactory() extension.Factory {
	return extension.NewFactory(Type, defaultConfigComponent, createExtension, Stability)
}

func defaultConfigComponent() component.Config { return defaultConfig() }

func createExtension(_ context.Context, _ extension.Settings, cfg component.Config) (extension.Extension, error) {
	return newExtension(cfg.(*Config)), nil
}
