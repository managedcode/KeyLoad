namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedGitHubFailureDiagnosticsProgram
{
    internal const string Source = """
        import { readFile, stat, access, writeFile } from 'node:fs/promises';
        import path from 'node:path';
        import { pathToFileURL } from 'node:url';
        import { createHash } from 'node:crypto';
        const [modulePath, root, failure] = process.argv.slice(1);
        const api = await import(pathToFileURL(modulePath));
        const { GH } = await import(pathToFileURL(path.join(path.dirname(modulePath), 'isolated-github-contract.mjs')));
        if (!['exit', 'stderr-bound', 'credential', 'credential-tail', 'retention-conflict'].includes(failure)) throw new Error('invalid cell');
        const binary = Buffer.from([0, 255, 128, 10, 1, 2, 3, 4]);
        const canary = 'private-native-stderr-canary';
        const oversized = ['stderr-bound', 'credential-tail'].includes(failure);
        const excluded = ['credential', 'credential-tail'].includes(failure);
        const stderr = failure === 'stderr-bound' ? Buffer.alloc(GH.stderrBytes + 1, 65)
          : failure === 'credential-tail' ? Buffer.concat([Buffer.alloc(GH.stderrBytes - 10, 65), Buffer.from(canary + '\n')])
          : Buffer.from(canary + '\nunexpected EOF\n');
        const previousToken = process.env.GH_TOKEN;
        if (excluded) process.env.GH_TOKEN = canary;
        const listeners = ['SIGTERM', 'SIGINT'].map(signal => process.listenerCount(signal));
        const child = `const { writeFileSync } = require('node:fs');
          const [marker, mode, limit] = process.argv.slice(1); writeFileSync(marker, String(process.pid));
          const done = () => process.stdout.write(Buffer.from([0,255,128,10,1,2,3,4]), () => { process.exitCode = mode === 'healthy' ? 0 : 1; });
          const token = Buffer.from('private-native-stderr-canary');
          const diagnostic = mode === 'stderr-bound' ? Buffer.alloc(Number(limit) + 1, 65)
            : mode === 'credential-tail' ? Buffer.concat([Buffer.alloc(Number(limit) - 10, 65), token, Buffer.from('\n')])
            : Buffer.from('private-native-stderr-canary\nunexpected EOF\n');
          if (mode !== 'healthy') process.stderr.write(diagnostic, done); else done();`;
        async function joined(marker) {
          const pid = Number(await readFile(marker, 'utf8'));
          if (!Number.isSafeInteger(pid) || pid <= 0) throw new Error('missing native child');
          try { process.kill(pid, 0); } catch (error) { if (error.code === 'ESRCH') return; throw error; }
          throw new Error('native child retained');
        }
        async function absent(file) {
          try { await access(file); } catch (error) { if (error.code === 'ENOENT') return; throw error; }
          throw new Error('unexpected diagnostic artifact');
        }
        try {
          const file = path.join(root, 'failed.bin'); const marker = path.join(root, 'failed.pid'); let refused = false; let originalFailure;
          const prior = Buffer.from('sealed-original-diagnostic');
          if (failure === 'retention-conflict') await writeFile(file + '.stderr', prior, { flag: 'wx', mode: 0o400 });
          try { await api.streamToFile(process.execPath, ['-e', child, marker, failure, String(GH.stderrBytes)], file, binary.length, 20000); }
          catch (error) { refused = true; originalFailure = error; }
          if (!refused) throw new Error('failed child admitted'); await joined(marker);
          const original = await readFile(file);
          let diagnosticBytes;
          if (failure === 'retention-conflict') {
            if (!(originalFailure instanceof AggregateError) || originalFailure.errors.length !== 2
              || originalFailure.errors[0].message !== GH.failure || originalFailure.errors[1].code !== 'EEXIST'
              || !(await readFile(file + '.stderr')).equals(prior)) throw new Error('primary or retained failure overwritten');
            await absent(file + '.native-process.json');
          } else {
          diagnosticBytes = await readFile(file + '.native-process.json');
          const diagnostic = JSON.parse(diagnosticBytes);
          if (original.length > binary.length || !original.equals(binary.subarray(0, original.length))
            || diagnostic.stderrBytes <= 0 || !/^[a-f0-9]{64}$/.test(diagnostic.stderrSha256)
            || diagnosticBytes.includes(Buffer.from(canary)) || diagnostic.exceeded !== oversized) throw new Error('failure evidence changed');
          if (oversized) {
            if (!excluded && diagnostic.stderrCapturedBytes !== GH.stderrBytes) throw new Error('stderr bound changed');
          } else if (diagnostic.exitCode !== 1 || diagnostic.signal !== null
            || diagnostic.stderrBytes !== stderr.length || diagnostic.stderrSha256 !== createHash('sha256').update(stderr).digest('hex')) throw new Error('original exit evidence changed');
          if (excluded) {
            if (!diagnostic.credentialExcluded || diagnostic.stderrCapturedBytes !== 0) throw new Error('credential retained');
            await absent(file + '.stderr');
          } else {
            if (diagnostic.credentialExcluded || !(await readFile(file + '.stderr')).equals(stderr.subarray(0, diagnostic.stderrCapturedBytes))
              || ((await stat(file + '.stderr')).mode & 0o777) !== 0o400) throw new Error('private stderr changed');
          }
          if (((await stat(file + '.native-process.json')).mode & 0o777) !== 0o400) throw new Error('diagnostic file not sealed');
          }
          if (((await stat(file)).mode & 0o777) !== 0o400) throw new Error('failure file not sealed');
          const healthyFile = path.join(root, 'healthy.bin'); const healthyMarker = path.join(root, 'healthy.pid');
          const healthy = await api.streamToFile(process.execPath, ['-e', child, healthyMarker, 'healthy', String(GH.stderrBytes)], healthyFile, binary.length, 20000);
          await joined(healthyMarker);
          if (!(await readFile(healthyFile)).equals(binary) || healthy.bytes !== binary.length
            || healthy.sha256 !== createHash('sha256').update(binary).digest('hex') || !(await readFile(file)).equals(original)
            || (diagnosticBytes !== undefined && !(await readFile(file + '.native-process.json')).equals(diagnosticBytes))) throw new Error('healthy continuation changed original evidence');
          await absent(healthyFile + '.stderr'); await absent(healthyFile + '.native-process.json');
          if (failure === 'retention-conflict' && !(await readFile(file + '.stderr')).equals(prior)) throw new Error('healthy replaced prior failure');
          if (['SIGTERM', 'SIGINT'].some((signal, index) => process.listenerCount(signal) !== listeners[index])) throw new Error('listener retained');
          process.stdout.write('accepted-diagnostic\n');
        } finally { if (previousToken === undefined) delete process.env.GH_TOKEN; else process.env.GH_TOKEN = previousToken; }
        """;
}
