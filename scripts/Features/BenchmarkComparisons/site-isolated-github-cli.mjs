import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { absolutePath } from './aggregate-files.mjs';
import { SITE_GH, requireSite } from './site-isolated-github-contract.mjs';
import { parseSiteIsolatedArguments } from './site-isolated-github-context.mjs';
import { captureSiteIsolatedEvidence, captureSiteIsolatedMetadata } from './site-isolated-github-capture.mjs';
import { validateSiteIsolatedInputs } from './site-isolated-github-proof.mjs';
import { verifySiteIsolatedFreshness } from './site-isolated-github-fresh.mjs';

export async function runSiteIsolatedGitHub(argv, environment = process.env) {
  const [command, ...args] = argv;
  if (command === 'capture') return captureSiteIsolatedEvidence({ environment, args });
  if (command === 'capture-metadata') return captureSiteIsolatedMetadata({ environment, args });
  if (command === 'verify-inputs') {
    const values = parseSiteIsolatedArguments(args, new Set(['input', 'receipt']), ['input', 'receipt']);
    return validateSiteIsolatedInputs({ input: absolutePath(values.input), receipt: absolutePath(values.receipt) });
  }
  requireSite(command === 'fresh');
  const values = parseSiteIsolatedArguments(args, new Set(['before', 'after']), ['before', 'after']);
  return verifySiteIsolatedFreshness({ before: absolutePath(values.before), after: absolutePath(values.after) });
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try { process.stdout.write(JSON.stringify({ ok: true, result: await runSiteIsolatedGitHub(process.argv.slice(2)) }) + '\n'); }
  catch { process.stdout.write(JSON.stringify({ ok: false, error: SITE_GH.failure }) + '\n'); process.exitCode = 1; }
}
