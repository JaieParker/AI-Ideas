// /weather skill helper.
//
// BR-SKILL-001 — $ARGUMENTS arrives single-quoted in the SKILL.md
// command, so this process receives the user's location as
// process.argv[2] (data, not code). No shell expansion happens here.
//
// BR-SKILL-005 — network egress allow-listed by host. The host is a
// string literal below; nothing in user input can redirect it.

const HOST = 'https://wttr.in';
const TIMEOUT_MS = 5000;

const arg = (process.argv[2] ?? '').trim();
const url = `${HOST}/${encodeURIComponent(arg)}?format=3`;

try {
  const response = await fetch(url, { signal: AbortSignal.timeout(TIMEOUT_MS) });
  if (!response.ok) {
    console.log(`weather lookup failed: HTTP ${response.status}`);
    process.exit(1);
  }
  const text = (await response.text()).trim();
  console.log(text || '(no weather data returned)');
} catch (err) {
  const reason = err instanceof Error ? err.message : String(err);
  console.log(`weather lookup failed: ${reason}`);
  process.exit(1);
}
