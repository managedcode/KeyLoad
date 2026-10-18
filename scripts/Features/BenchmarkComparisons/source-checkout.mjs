import { assertCleanSourceRevision } from './source-context.mjs';
import { message, processLimit } from './native-process-contracts.mjs';
import { runBounded } from './native-process.mjs';

export async function verifySourceCheckout(context) {
  const options = { cwd: context.workspace, timeoutMs: processLimit.inspectTimeoutMs };
  const revision = await runBounded('git', ['rev-parse', 'HEAD'], options);
  if (!revision.success) throw new Error(message.invalidWorkspace);
  const diff = await runBounded('git', ['diff', '--quiet', 'HEAD', '--'], options);
  const status = await runBounded('git', ['status', '--porcelain', '--untracked-files=all'], options);
  if (!status.success || diff.timedOut || diff.outputLimitExceeded || diff.spawnFailed || diff.signal !== null)
    throw new Error(message.invalidWorkspace);
  assertCleanSourceRevision(context, revision.stdout, diff.code, status.stdout);
}
