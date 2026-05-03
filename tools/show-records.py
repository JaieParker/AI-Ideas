#!/usr/bin/env python3
"""Render output/telemetry.jsonl resource attributes per record."""
import json, sys

def value_str(v):
    if "stringValue" in v: return v["stringValue"]
    if "intValue"    in v: return v["intValue"]
    if "boolValue"   in v: return v["boolValue"]
    return next(iter(v.values()), None)

with open("output/telemetry.jsonl") as f:
    for i, line in enumerate(f, 1):
        data = json.loads(line)
        rs_list = (data.get("resourceSpans")
                   or data.get("resourceMetrics")
                   or data.get("resourceLogs")
                   or [])
        kind = ("traces" if data.get("resourceSpans")
                else "metrics" if data.get("resourceMetrics")
                else "logs"   if data.get("resourceLogs")
                else "?")
        print(f"---- record {i} ({kind}) ----")
        for rs in rs_list:
            attrs = rs.get("resource", {}).get("attributes", [])
            for a in attrs:
                key = a.get("key", "?")
                val = value_str(a.get("value", {}))
                print(f"  {key:30} = {val}")
        print()
