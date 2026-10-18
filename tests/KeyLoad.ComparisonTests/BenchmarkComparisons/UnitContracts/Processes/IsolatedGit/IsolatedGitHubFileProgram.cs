namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedGitHubFileProgram
{
    internal const string Source = """
        import { writeFile, readFile, symlink } from 'node:fs/promises';
        import path from 'node:path';
        import { pathToFileURL } from 'node:url';
        import { createHash } from 'node:crypto';
        const [modulePath, root, corruption] = process.argv.slice(1);
        const api = await import(pathToFileURL(modulePath));
        const inventory = await import(pathToFileURL(path.join(path.dirname(modulePath), 'isolated-github-zip.mjs')));
        const file = path.join(root, 'archive.zip');
        const bytes = Buffer.from([0, 255, 128, 10, 1, 2, 3, 4]);
        const artifact = { size_in_bytes: bytes.length, digest: 'sha256:' + createHash('sha256').update(bytes).digest('hex') };
        if (corruption === 'existing') await writeFile(file, 'original');
        try {
          if (corruption === 'timeout') await api.streamToFile(process.execPath, ['-e', 'setInterval(() => {}, 1000)'], file, 1024, 30);
          else await api.streamToFile(process.execPath, ['-e', 'process.stdout.write(Buffer.from([0,255,128,10,1,2,3,4]))'], file,
            corruption === 'bound' ? 4 : 1024, 10000);
          if (corruption === 'hash') artifact.digest = 'sha256:' + 'a'.repeat(64);
          if (corruption === 'size') artifact.size_in_bytes++;
          let selected = file;
          if (corruption === 'symlink') { selected = path.join(root, 'link.zip'); await symlink(file, selected); }
          await api.validateDownloadedArchive(selected, artifact, 1024);
          let entries = 'session.json\nlogs/node1.log\n';
          if (corruption === 'duplicate-entry') entries += 'session.json\n';
          if (corruption === 'missing-entry') entries = 'logs/node1.log\n';
          if (corruption === 'unsafe-entry') entries += '../foreign.json\n';
          inventory.validateZipInventory(entries, ['session.json']);
          if (!(await readFile(file)).equals(bytes)) throw new Error('binary changed');
          process.stdout.write('accepted\n');
        } catch {
          if (corruption === 'existing' && await readFile(file, 'utf8') !== 'original') throw new Error('overwritten');
          process.stdout.write('rejected\n'); process.exitCode = 1;
        }
        """;

    internal const string LargeSource = """
        import { readFile, stat } from 'node:fs/promises';
        import path from 'node:path';
        import { pathToFileURL } from 'node:url';
        import { createHash } from 'node:crypto';
        const [modulePath, root, lengthText, writer] = process.argv.slice(1);
        const length = Number(lengthText);
        if (![219259, 1048576, 8388608].includes(length) || !['async', 'sync'].includes(writer)) throw new Error('invalid cell');
        const api = await import(pathToFileURL(modulePath));
        const pattern = Buffer.from([0, 255, 128, 10, 1, 2, 3, 4, 13, 127, 64, 32, 222, 173, 190, 239]);
        const expected = Buffer.alloc(length); expected.fill(pattern);
        const digest = createHash('sha256').update(expected).digest('hex');
        const listeners = ['SIGTERM', 'SIGINT'].map(signal => process.listenerCount(signal));
        const child = `
          const { writeFileSync, writeSync } = require('node:fs');
          const [lengthText, writer, marker] = process.argv.slice(1);
          const bytes = Buffer.alloc(Number(lengthText));
          bytes.fill(Buffer.from([0,255,128,10,1,2,3,4,13,127,64,32,222,173,190,239]));
          writeFileSync(marker, String(process.pid));
          if (writer === 'async') process.stdout.write(bytes);
          else { let offset = 0; while (offset < bytes.length) offset += writeSync(1, bytes, offset, bytes.length - offset); process.exit(0); }
        `;
        async function joined(marker) {
          const pid = Number(await readFile(marker, 'utf8'));
          if (!Number.isSafeInteger(pid) || pid <= 0) throw new Error('missing original child');
          try { process.kill(pid, 0); } catch (error) { if (error.code === 'ESRCH') return; throw error; }
          throw new Error('child not joined');
        }
        async function capture(name, size, maximum) {
          const file = path.join(root, name + '.bin');
          const marker = path.join(root, name + '.pid');
          const result = await api.streamToFile(process.execPath, ['-e', child, String(size), writer, marker], file, maximum, 20000);
          await joined(marker);
          return { file, result };
        }
        const complete = await capture('complete', length, length);
        if (complete.result.bytes !== length || complete.result.sha256 !== digest
          || !(await readFile(complete.file)).equals(expected)) throw new Error('original large bytes changed');
        await api.validateDownloadedArchive(complete.file, { size_in_bytes: length, digest: 'sha256:' + digest }, length);
        let refused = false;
        const rejectedFile = path.join(root, 'one-over.bin');
        const rejectedMarker = path.join(root, 'one-over.pid');
        try {
          await api.streamToFile(process.execPath, ['-e', child, String(length), writer, rejectedMarker], rejectedFile, length - 1, 20000);
        } catch { refused = true; }
        if (!refused) throw new Error('one-over admitted');
        await joined(rejectedMarker);
        const partial = await readFile(rejectedFile);
        if (partial.length === 0 || partial.length > length - 1 || !partial.equals(expected.subarray(0, partial.length))) throw new Error('bound or original prefix changed');
        const healthy = await capture('healthy', length, length);
        if (healthy.result.bytes !== length || healthy.result.sha256 !== digest
          || !(await readFile(healthy.file)).equals(expected)) throw new Error('healthy continuation changed');
        await api.validateDownloadedArchive(healthy.file, { size_in_bytes: length, digest: 'sha256:' + digest }, length);
        for (const file of [complete.file, rejectedFile, healthy.file]) {
          if (((await stat(file)).mode & 0o777) !== 0o400) throw new Error('capture not sealed');
        }
        if (['SIGTERM', 'SIGINT'].some((signal, index) => process.listenerCount(signal) !== listeners[index])) throw new Error('listener retained');
        process.stdout.write('accepted-large\n');
        """;
}
