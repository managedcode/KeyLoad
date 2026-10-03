namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

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
          let entries = 'worker.json\nlogs/node1.log\n';
          if (corruption === 'duplicate-entry') entries += 'worker.json\n';
          if (corruption === 'missing-entry') entries = 'logs/node1.log\n';
          if (corruption === 'unsafe-entry') entries += '../foreign.json\n';
          inventory.validateZipInventory(entries, ['worker.json']);
          if (!(await readFile(file)).equals(bytes)) throw new Error('binary changed');
          process.stdout.write('accepted\n');
        } catch {
          if (corruption === 'existing' && await readFile(file, 'utf8') !== 'original') throw new Error('overwritten');
          process.stdout.write('rejected\n'); process.exitCode = 1;
        }
        """;
}
