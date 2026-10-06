namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class NativeBenchmarkProgressNodeProgram
{
    internal const string PrivateCanary = "private-canary-never-relayed";
    internal const string MeasureLine = "KeyLoadBenchmarkProgress phase=measure repetition=1 completed=2 total=4 failed=0 elapsedSeconds=0.100";
    internal const string CompleteLine = "KeyLoadBenchmarkProgress phase=complete repetition=1 completed=4 total=4 failed=0 elapsedSeconds=0.200";
    internal const string Source = """
        import assert from 'node:assert/strict';
        import { writeFile, readFile, symlink } from 'node:fs/promises';
        import path from 'node:path';
        import { pathToFileURL } from 'node:url';
        const [modulePath, scenario, root] = process.argv.slice(1);
        const api = await import(pathToFileURL(modulePath));
        const file = path.join(root, 'progress.log');
        const first = 'KeyLoadBenchmarkProgress phase=measure repetition=1 completed=2 total=4 failed=0 elapsedSeconds=0.100';
        const last = 'KeyLoadBenchmarkProgress phase=complete repetition=1 completed=4 total=4 failed=0 elapsedSeconds=0.200';
        if (scenario === 'live-exit') {
          const source = `const fs = require('node:fs');
            fs.writeFileSync(process.argv[1], ${JSON.stringify(first)});
            setTimeout(() => { fs.writeFileSync(process.argv[1], ${JSON.stringify(last)}); process.exit(7); }, 250);`;
          const result = await api.runProgressProcess(process.execPath, ['--eval', source, file], root, file, 20);
          assert.equal(result, 7);
          assert.equal(await readFile(file, 'utf8'), last);
        } else if (scenario === 'cancel') {
          const receipt = path.join(root, 'cancelled');
          const source = `const fs = require('node:fs');
            process.on('SIGTERM', () => { fs.writeFileSync(process.argv[2], 'observed'); process.exit(0); });
            fs.writeFileSync(process.argv[1], String(process.pid));
            setInterval(() => {}, 1000);`;
          const active = api.runProgressProcess(process.execPath, ['--eval', source, file, receipt], root, file, 20);
          let pid;
          const deadline = Date.now() + 5000;
          while (!pid && Date.now() < deadline) {
            try { pid = Number(await readFile(file, 'utf8')); } catch {}
            if (!pid) await new Promise(resolve => setTimeout(resolve, 10));
          }
          assert.ok(pid);
          process.kill(process.pid, 'SIGTERM');
          assert.equal(await active, 143);
          assert.equal(await readFile(receipt, 'utf8'), 'observed');
          assert.throws(() => process.kill(pid, 0), { code: 'ESRCH' });
        } else if (scenario === 'invalid') {
          for (const line of [first + ' private-canary-never-relayed', first.replace('failed=0', 'failed=3'),
            first.replace('completed=2', 'completed=5'), first.replace('elapsedSeconds=0.100', 'elapsedSeconds=Infinity'),
            first.replace('completed=2', 'completed=2147483648'), first.replace('phase=measure', 'phase=secret'),
            first.replace('repetition=1', 'repetition=-1')]) {
            await writeFile(file, line);
            assert.equal(await api.readProgress(file), null);
          }
          await writeFile(file, first);
          assert.equal(await api.readProgress(file), first);
        } else if (scenario === 'links-bounds') {
          const target = path.join(root, 'target');
          await writeFile(target, first);
          await symlink(target, file);
          assert.equal(await api.readProgress(file), null);
          const oversized = path.join(root, 'oversized');
          await writeFile(oversized, first + ' '.repeat(513));
          assert.equal(await api.readProgress(oversized), null);
          assert.equal(await api.readProgress(path.join(root, 'missing')), null);
        } else if (scenario === 'no-cell') {
          delete process.env.KEYLOAD_COMPARISON_CELL_ID;
          await assert.rejects(api.runWorkload(), /identity is invalid/);
          process.env.KEYLOAD_COMPARISON_CELL_ID = '../../outside';
          await assert.rejects(api.runWorkload(), /identity is invalid/);
        } else throw new Error('Unknown scenario');
        process.stdout.write('accepted:' + scenario + '\n');
        """;
}
