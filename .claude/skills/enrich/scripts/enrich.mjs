// /enrich skill helper.
//
// BR-SKILL-001 — $ARGUMENTS arrives single-quoted in SKILL.md, so the
// arg string lands here as one element of process.argv. No shell
// expansion happens before this script runs.
//
// BR-SKILL-005 — egress is allow-listed: only the loopback collector
// control API on 127.0.0.1:13133.
//
// The collector validates input itself (BR-ENRICH-001/002), so this
// helper is a thin HTTP client. It deliberately does *not* duplicate
// validation — that would be deterministic work in two places.

const CONTROL_API = 'http://127.0.0.1:13133';
const TIMEOUT_MS = 3000;

class HttpError extends Error {
  constructor(status, detail) { super(`HTTP ${status}: ${detail}`); this.status = status; }
}

const sessionId = (process.argv[2] ?? '').trim();
const argString = (process.argv[3] ?? '').trim();

if (!sessionId) {
  console.log('enrich failed: no session id provided');
  process.exit(2);
}

const verb = parseVerb(argString);

try {
  const result = await dispatch(verb);
  console.log(result);
} catch (err) {
  console.log(formatError(err));
  process.exit(1);
}

// ---------------------------------------------------------------------

function parseVerb(s) {
  if (s.length === 0) {
    return { kind: 'usage' };
  }
  if (s.startsWith('--')) {
    const m = s.match(/^(--[a-z]+)(?:\s+(.+))?$/);
    if (!m) return { kind: 'usage' };
    const flag = m[1];
    const rest = (m[2] ?? '').trim();
    if (flag === '--show')   return { kind: 'show' };
    if (flag === '--clear')  return { kind: 'clear' };
    if (flag === '--remove') return rest ? { kind: 'remove', key: rest } : { kind: 'usage' };
    return { kind: 'usage' };
  }
  // key + value form. Value is everything after the first whitespace.
  const m = s.match(/^(\S+)(?:\s+(.+))?$/);
  if (!m || !m[2]) return { kind: 'usage' };
  return { kind: 'set', key: m[1], value: m[2].trim() };
}

async function dispatch(verb) {
  const url = `${CONTROL_API}/sessions/${encodeURIComponent(sessionId)}/enrichments`;

  switch (verb.kind) {
    case 'usage':
      return 'usage: /enrich <key> <value> | --remove <key> | --clear | --show';

    case 'show': {
      const r = await get(url);
      const map = await r.json();
      const keys = Object.keys(map);
      if (keys.length === 0) return '(no enrichments set on this session)';
      return keys.sort().map(k => `${k}=${map[k]}`).join('\n');
    }

    case 'set': {
      const r = await post(url, { op: 'set', key: verb.key, value: verb.value });
      const body = await r.json();
      const warns = (body.warnings ?? []).map(w => `\n  warning: ${w}`).join('');
      return `set ${verb.key}=${verb.value}${warns}`;
    }

    case 'remove': {
      await post(url, { op: 'remove', key: verb.key });
      return `removed ${verb.key}`;
    }

    case 'clear': {
      await post(url, { op: 'clear' });
      return 'cleared all per-session enrichments';
    }
  }
}

async function post(url, body) {
  const r = await fetch(url, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body),
    signal: AbortSignal.timeout(TIMEOUT_MS),
  });
  if (!r.ok) {
    const detail = await r.text();
    throw new HttpError(r.status, detail);
  }
  return r;
}

async function get(url) {
  const r = await fetch(url, { signal: AbortSignal.timeout(TIMEOUT_MS) });
  if (!r.ok) {
    const detail = await r.text();
    throw new HttpError(r.status, detail);
  }
  return r;
}

function formatError(err) {
  if (err instanceof HttpError) return `enrich failed: ${err.message}`;
  const msg = err instanceof Error ? err.message : String(err);
  if (/ECONNREFUSED|fetch failed|connect/i.test(msg)) {
    return 'enrich failed: collector control API not reachable on 127.0.0.1:13133. ' +
           'Run /otel to start the collector and try again.';
  }
  return `enrich failed: ${msg}`;
}
