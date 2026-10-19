import { spawn } from 'node:child_process';
import { runtimeBundle, requireRuntime } from './compiled-test-bundle-contract.mjs';

export async function runBundle(command, args, cwd, maximumOutput = runtimeBundle.maximumOutputBytes) {
  requireRuntime(['git', 'tar', 'dotnet'].includes(command));
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, { cwd, stdio: ['ignore', 'pipe', 'pipe'], detached: true });
    const stdout = [];
    const stderr = [];
    let bytes = 0;
    let errorBytes = 0;
    let failed = false;
    let force;
    const stop = () => {
      failed = true;
      if (Number.isSafeInteger(child.pid)) {
        try { process.kill(-child.pid, 'SIGTERM'); } catch (error) { if (error.code !== 'ESRCH') failed = true; }
        force ??= setTimeout(() => {
          try { process.kill(-child.pid, 'SIGKILL'); } catch { /* The original child may already be joined. */ }
        }, 5000);
      }
    };
    const timeout = setTimeout(stop, runtimeBundle.timeoutMs);
    process.on('SIGTERM', stop);
    process.on('SIGINT', stop);
    child.stdout.on('data', chunk => {
      bytes += chunk.length;
      if (bytes > maximumOutput) stop();
      else stdout.push(chunk);
    });
    child.stderr.on('data', chunk => {
      errorBytes += chunk.length;
      if (errorBytes > 262144) stop();
      else stderr.push(chunk);
    });
    child.on('error', () => { failed = true; });
    child.on('close', (code, signal) => {
      clearTimeout(timeout);
      clearTimeout(force);
      process.off('SIGTERM', stop);
      process.off('SIGINT', stop);
      try {
        requireRuntime(!failed && signal === null && code === 0);
        resolve(Buffer.concat(stdout));
      } catch (error) {
        process.stderr.write(Buffer.concat(stderr));
        reject(error);
      }
    });
  });
}
