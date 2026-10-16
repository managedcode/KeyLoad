import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { SESSION, requireSession } from './session-contract.mjs';
import { parseSessionSiteArguments } from './site-session-context.mjs';
import { captureSessionSite } from './site-session-github.mjs';
import { verifySessionSiteInputs, verifySessionSiteFreshness } from './site-session-proof.mjs';

// This remains the single Website intake entry point; historical per-cell schemas are not admitted.
export async function runSiteIsolatedGitHub(argv, environment = process.env) {
  const [command, ...args] = argv;
  if (command === 'capture' || command === 'capture-metadata')
    return captureSessionSite({ environment, args, download: command === 'capture' });
  if (command === 'verify-inputs') {
    const values = parseSessionSiteArguments(args, ['input', 'receipt'], ['input', 'receipt']);
    requireSession(path.isAbsolute(values.input) && path.isAbsolute(values.receipt));
    return verifySessionSiteInputs({ input: path.resolve(values.input), receipt: path.resolve(values.receipt) });
  }
  requireSession(command === 'fresh');
  const values = parseSessionSiteArguments(args, ['before', 'after'], ['before', 'after']);
  requireSession(path.isAbsolute(values.before) && path.isAbsolute(values.after));
  return verifySessionSiteFreshness({ before: path.resolve(values.before), after: path.resolve(values.after) });
}
if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try { process.stdout.write(JSON.stringify({ ok: true, result: await runSiteIsolatedGitHub(process.argv.slice(2)) }) + '\n'); }
  catch { process.stdout.write(JSON.stringify({ ok: false, error: SESSION.failure }) + '\n'); process.exitCode = 1; }
}
