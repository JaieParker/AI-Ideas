#!/usr/bin/env bash
# Integration smoke test for the Go OTel collector.
#
# Drives synthetic OTLP traces through the running collector and
# asserts the JSONL output contains both persistent and per-session
# enrichments, plus the JA-0001 → JA-0002 transition the demo
# advertises.
#
# Pre-conditions:
#   - port 4318 free (no other OTLP receiver listening)
#   - port 13133 / 13134 free
#   - go and ocb available (verified by build steps; this script
#     only runs the prebuilt binary)

set -euo pipefail
cd "$(dirname "$0")/../.."

BIN=./dist/windows-amd64/claude-otel-collector.exe
[ -f "$BIN" ] || { echo "missing collector binary at $BIN — run ocb first"; exit 2; }

mkdir -p output
rm -f output/telemetry.jsonl persistent-enrichments.json

"$BIN" --config=config.yaml > /tmp/collector.log 2>&1 &
PID=$!
trap "kill $PID 2>/dev/null; wait 2>/dev/null" EXIT
sleep 3

# Health
curl -fsS http://127.0.0.1:13134/ > /dev/null

# Persistent: user/workstation/version
for kv in 'user:Jaie' 'workstation:LightningBlue' 'version:0.001'; do
  k=${kv%%:*}; v=${kv#*:}
  curl -fsS -X POST http://127.0.0.1:13133/persistent-enrichments \
    -H 'Content-Type: application/json' \
    -d "{\"op\":\"set\",\"key\":\"$k\",\"value\":\"$v\"}" > /dev/null
done

# Session JA-DEMO with ticket=JA-0001
curl -fsS -X POST http://127.0.0.1:13133/sessions/JA-DEMO/enrichments \
  -H 'Content-Type: application/json' \
  -d '{"op":"set","key":"ticket.id","value":"JA-0001"}' > /dev/null

# Send a synthetic span
TRACE_PAYLOAD='{
  "resourceSpans": [{
    "resource": { "attributes": [
      { "key": "service.name", "value": { "stringValue": "smoke-test" } },
      { "key": "session.id",   "value": { "stringValue": "JA-DEMO" } }
    ]},
    "scopeSpans": [{
      "scope": { "name": "smoke" },
      "spans": [{ "traceId": "4bf92f3577b34da6a3ce929d0e0e4736", "spanId": "00f067aa0ba902b7",
                   "name": "smoke.span", "kind": 1,
                   "startTimeUnixNano": "1746180000000000000",
                   "endTimeUnixNano":   "1746180000010000000" }]
    }]
  }]
}'
curl -fsS -X POST http://127.0.0.1:4318/v1/traces \
  -H 'Content-Type: application/json' \
  -d "$TRACE_PAYLOAD" > /dev/null

# Change ticket to JA-0002 and resend
curl -fsS -X POST http://127.0.0.1:13133/sessions/JA-DEMO/enrichments \
  -H 'Content-Type: application/json' \
  -d '{"op":"set","key":"ticket.id","value":"JA-0002"}' > /dev/null
curl -fsS -X POST http://127.0.0.1:4318/v1/traces \
  -H 'Content-Type: application/json' \
  -d "$TRACE_PAYLOAD" > /dev/null

sleep 1

# Assertions
fail=0
check() {
  local name=$1 cmd=$2
  if eval "$cmd" > /dev/null; then
    echo "  ✓ $name"
  else
    echo "  ✗ $name"
    fail=1
  fi
}

echo "Assertions on output/telemetry.jsonl:"
check "BR-ENRICH-007 — persistent user=Jaie present"        'grep -q "Jaie" output/telemetry.jsonl'
check "BR-ENRICH-007 — persistent workstation=LightningBlue" 'grep -q "LightningBlue" output/telemetry.jsonl'
check "BR-ENRICH-007 — persistent version=0.001"            'grep -q "0.001" output/telemetry.jsonl'
check "BR-ENRICH-008 — per-session JA-0001 stamped"         'grep -q "JA-0001" output/telemetry.jsonl'
check "BR-ENRICH-005 — JA-0002 appears after JA-0001"       'grep -q "JA-0002" output/telemetry.jsonl'

[ $fail -eq 0 ] && echo "ALL GREEN" || { echo "FAILED"; exit 1; }
