// Copyright Jaie Parker
// SPDX-License-Identifier: Apache-2.0

package enrichmentprocessor

import (
	"context"
	"errors"

	"go.opentelemetry.io/collector/component"
	"go.opentelemetry.io/collector/consumer"
	"go.opentelemetry.io/collector/pdata/pcommon"
	"go.opentelemetry.io/collector/pdata/plog"
	"go.opentelemetry.io/collector/pdata/pmetric"
	"go.opentelemetry.io/collector/pdata/ptrace"
	"go.opentelemetry.io/collector/processor"
	"go.opentelemetry.io/collector/processor/processorhelper"

	enrichctl "github.com/jaie/claude-otel-collector/components/enrichmentctlextension"
)

const sessionIDKey = "session.id"

// processorCapabilities — we mutate attributes in place.
var processorCapabilities = consumer.Capabilities{MutatesData: true}

var (
	Type      = component.MustNewType("enrichment")
	Stability = component.StabilityLevelDevelopment
)

// NewFactory returns the processor factory.
func NewFactory() processor.Factory {
	return processor.NewFactory(
		Type,
		defaultConfigComponent,
		processor.WithTraces(createTracesProcessor, Stability),
		processor.WithLogs(createLogsProcessor, Stability),
		processor.WithMetrics(createMetricsProcessor, Stability),
	)
}

func defaultConfigComponent() component.Config { return defaultConfig() }

// shared state between traces / logs / metrics processors of the
// same instance.
type enrichmentProc struct {
	cfg   *Config
	store *enrichctl.Store
}

// resolveStore looks up the enrichmentctl extension by ID at Start
// time and saves the *Store for fast access in Consume*.
//
// BR-ENRICH-012 — readers do a single atomic Load() on every batch.
func (p *enrichmentProc) start(_ context.Context, host component.Host) error {
	exts := host.GetExtensions()
	for id, ext := range exts {
		if id == p.cfg.ExtensionID {
			ee, ok := ext.(*enrichctl.Extension)
			if !ok {
				return errors.New("configured extension is not enrichmentctl")
			}
			p.store = ee.Store()
			return nil
		}
	}
	return errors.New("enrichmentctl extension not configured at id " + p.cfg.ExtensionID.String())
}

// processTraces stamps every span with the per-session and persistent
// attributes, in the order required by BR-ENRICH-008 (persistent
// first, per-session overrides). Sessions whose collection is
// disabled (BR-ENRICH-004) have their resource-spans dropped
// entirely.
func (p *enrichmentProc) processTraces(_ context.Context, td ptrace.Traces) (ptrace.Traces, error) {
	snap := p.store.Load()
	rss := td.ResourceSpans()
	for i := rss.Len() - 1; i >= 0; i-- {
		rs := rss.At(i)
		// Find session.id (first match wins).
		sid, found := findSessionID(rs.Resource().Attributes(), rs)
		if found && !snap.SessionEnabled(sid) {
			rss.RemoveIf(func(x ptrace.ResourceSpans) bool { return x == rs })
			continue
		}
		stampResourceSpans(rs, snap, sid, found)
	}
	return td, nil
}

func (p *enrichmentProc) processLogs(_ context.Context, ld plog.Logs) (plog.Logs, error) {
	snap := p.store.Load()
	rls := ld.ResourceLogs()
	for i := rls.Len() - 1; i >= 0; i-- {
		rl := rls.At(i)
		sid, found := findSessionIDLogs(rl.Resource().Attributes(), rl)
		if found && !snap.SessionEnabled(sid) {
			rls.RemoveIf(func(x plog.ResourceLogs) bool { return x == rl })
			continue
		}
		stampResourceLogs(rl, snap, sid, found)
	}
	return ld, nil
}

func (p *enrichmentProc) processMetrics(_ context.Context, md pmetric.Metrics) (pmetric.Metrics, error) {
	snap := p.store.Load()
	rms := md.ResourceMetrics()
	for i := rms.Len() - 1; i >= 0; i-- {
		rm := rms.At(i)
		sid, found := findSessionIDMetrics(rm.Resource().Attributes(), rm)
		if found && !snap.SessionEnabled(sid) {
			rms.RemoveIf(func(x pmetric.ResourceMetrics) bool { return x == rm })
			continue
		}
		stampResourceMetrics(rm, snap, sid, found)
	}
	return md, nil
}

// ---------------- stamping helpers ----------------

func stampMap(attrs pcommon.Map, snap *enrichctl.Snapshot, sid string) {
	// BR-ENRICH-008 — persistent first, then per-session (overrides).
	for k, v := range snap.Persistent {
		attrs.PutStr(k, v)
	}
	if sid != "" {
		if sm := snap.SessionAttrs(sid); sm != nil {
			for k, v := range sm {
				attrs.PutStr(k, v)
			}
		}
	}
}

func stampResourceSpans(rs ptrace.ResourceSpans, snap *enrichctl.Snapshot, sid string, found bool) {
	stampMap(rs.Resource().Attributes(), snap, sid)
	for i := 0; i < rs.ScopeSpans().Len(); i++ {
		ss := rs.ScopeSpans().At(i)
		for j := 0; j < ss.Spans().Len(); j++ {
			span := ss.Spans().At(j)
			if !found {
				if v, ok := span.Attributes().Get(sessionIDKey); ok {
					sid = v.AsString()
					found = true
				}
			}
			stampMap(span.Attributes(), snap, sid)
		}
	}
	_ = found
}

func stampResourceLogs(rl plog.ResourceLogs, snap *enrichctl.Snapshot, sid string, found bool) {
	stampMap(rl.Resource().Attributes(), snap, sid)
	for i := 0; i < rl.ScopeLogs().Len(); i++ {
		sl := rl.ScopeLogs().At(i)
		for j := 0; j < sl.LogRecords().Len(); j++ {
			lr := sl.LogRecords().At(j)
			if !found {
				if v, ok := lr.Attributes().Get(sessionIDKey); ok {
					sid = v.AsString()
					found = true
				}
			}
			stampMap(lr.Attributes(), snap, sid)
		}
	}
	_ = found
}

func stampResourceMetrics(rm pmetric.ResourceMetrics, snap *enrichctl.Snapshot, sid string, found bool) {
	stampMap(rm.Resource().Attributes(), snap, sid)
	for i := 0; i < rm.ScopeMetrics().Len(); i++ {
		sm := rm.ScopeMetrics().At(i)
		for j := 0; j < sm.Metrics().Len(); j++ {
			m := sm.Metrics().At(j)
			stampMetricDataPoints(m, snap, sid, found)
		}
	}
}

func stampMetricDataPoints(m pmetric.Metric, snap *enrichctl.Snapshot, sid string, found bool) {
	switch m.Type() {
	case pmetric.MetricTypeGauge:
		dps := m.Gauge().DataPoints()
		for i := 0; i < dps.Len(); i++ {
			dp := dps.At(i)
			s := sid
			if !found {
				if v, ok := dp.Attributes().Get(sessionIDKey); ok {
					s = v.AsString()
				}
			}
			stampMap(dp.Attributes(), snap, s)
		}
	case pmetric.MetricTypeSum:
		dps := m.Sum().DataPoints()
		for i := 0; i < dps.Len(); i++ {
			dp := dps.At(i)
			s := sid
			if !found {
				if v, ok := dp.Attributes().Get(sessionIDKey); ok {
					s = v.AsString()
				}
			}
			stampMap(dp.Attributes(), snap, s)
		}
	case pmetric.MetricTypeHistogram:
		dps := m.Histogram().DataPoints()
		for i := 0; i < dps.Len(); i++ {
			dp := dps.At(i)
			s := sid
			if !found {
				if v, ok := dp.Attributes().Get(sessionIDKey); ok {
					s = v.AsString()
				}
			}
			stampMap(dp.Attributes(), snap, s)
		}
	}
}

// ---------------- session.id discovery ----------------

func findSessionID(resAttrs pcommon.Map, rs ptrace.ResourceSpans) (string, bool) {
	if v, ok := resAttrs.Get(sessionIDKey); ok {
		return v.AsString(), true
	}
	// scan first span's attrs
	for i := 0; i < rs.ScopeSpans().Len(); i++ {
		ss := rs.ScopeSpans().At(i)
		if ss.Spans().Len() == 0 {
			continue
		}
		if v, ok := ss.Spans().At(0).Attributes().Get(sessionIDKey); ok {
			return v.AsString(), true
		}
	}
	return "", false
}

func findSessionIDLogs(resAttrs pcommon.Map, rl plog.ResourceLogs) (string, bool) {
	if v, ok := resAttrs.Get(sessionIDKey); ok {
		return v.AsString(), true
	}
	for i := 0; i < rl.ScopeLogs().Len(); i++ {
		sl := rl.ScopeLogs().At(i)
		if sl.LogRecords().Len() == 0 {
			continue
		}
		if v, ok := sl.LogRecords().At(0).Attributes().Get(sessionIDKey); ok {
			return v.AsString(), true
		}
	}
	return "", false
}

func findSessionIDMetrics(resAttrs pcommon.Map, rm pmetric.ResourceMetrics) (string, bool) {
	if v, ok := resAttrs.Get(sessionIDKey); ok {
		return v.AsString(), true
	}
	// data-point search would be exhaustive; for v1 we trust resource for metrics.
	_ = rm
	return "", false
}

// ---------------- factory create funcs ----------------

func createTracesProcessor(ctx context.Context, set processor.Settings, cfg component.Config, next consumer.Traces) (processor.Traces, error) {
	p := &enrichmentProc{cfg: cfg.(*Config)}
	return processorhelper.NewTraces(ctx, set, cfg, next, p.processTraces,
		processorhelper.WithStart(p.start),
		processorhelper.WithCapabilities(processorCapabilities))
}

func createLogsProcessor(ctx context.Context, set processor.Settings, cfg component.Config, next consumer.Logs) (processor.Logs, error) {
	p := &enrichmentProc{cfg: cfg.(*Config)}
	return processorhelper.NewLogs(ctx, set, cfg, next, p.processLogs,
		processorhelper.WithStart(p.start),
		processorhelper.WithCapabilities(processorCapabilities))
}

func createMetricsProcessor(ctx context.Context, set processor.Settings, cfg component.Config, next consumer.Metrics) (processor.Metrics, error) {
	p := &enrichmentProc{cfg: cfg.(*Config)}
	return processorhelper.NewMetrics(ctx, set, cfg, next, p.processMetrics,
		processorhelper.WithStart(p.start),
		processorhelper.WithCapabilities(processorCapabilities))
}
