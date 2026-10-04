using System.Text.Json;
using KeyLoad.UnitTests.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class Native5ServerPaxVerifierNodeProcess
{
    private const string Probe = """
        import { createReadStream } from 'node:fs';
        import { chmod, lstat, mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
        import os from 'node:os';
        import path from 'node:path';
        import { createHash } from 'node:crypto';
        import { pathToFileURL } from 'node:url';
        const proof = await import(pathToFileURL(process.argv[1]).href);
        const sourceBuilder = await import(pathToFileURL(process.argv[2]).href);
        const input = JSON.parse(process.argv[3]);
        const root = await mkdtemp(path.join(os.tmpdir(), 'keyload-native5-pax-'));
        const evidence = path.join(root, 'evidence');
        await chmod(root, 0o700);
        await mkdir(evidence, { mode: 0o700 });
        let source;
        let primary;
        try {
          source = await sourceBuilder.createNative5ServerSource({ runnerTemp: root, runId: '1', runAttempt: '1',
            workspace: process.cwd(), evidenceDirectory: evidence });
          const archive = await readFile(source.archivePath);
          const inventory = await readFile(path.join(evidence, 'prior-server-source-inventory.json'));
          const receipt = JSON.parse(Buffer.from(input.receipt, 'base64').toString('utf8'));
          const manifest = Buffer.from(input.manifest, 'base64');
          receipt.imageSource.archiveSha256 = sha256(archive);
          receipt.imageSource.archiveBytes = archive.length;
          receipt.imageSource.inventoryFileSha256 = sha256(inventory);
          const producer = receipt.producer;
          validatePaxSource(archive);
          const cases = makeCases();
          const results = [];
          for (const item of cases) {
            results.push(await verifyCase({ name: item.name, bytes: item.create(archive) }, receipt, manifest, inventory, producer));
          }
          const genuineUnchanged = await fileSha(source.archivePath) === source.archiveSha256;
          for (const result of results) result.Preserved = result.Preserved && genuineUnchanged;
          process.stdout.write(JSON.stringify(results));
        } catch (error) {
          if (error?.preserveOwnedPaths === true) throw error;
          primary = error;
        }
        const cleanup = [];
        if (source) { try { await sourceBuilder.cleanupNative5ServerSource(source); } catch (error) { cleanup.push(error); } }
        try { await rm(root, { recursive: true, force: true }); } catch (error) { cleanup.push(error); }
        if (primary && cleanup.length) throw new AggregateError([primary, ...cleanup], 'PAX proof and cleanup failed.');
        if (primary) throw primary;
        if (cleanup.length) throw new AggregateError(cleanup, 'Owned PAX proof cleanup failed.');

        function makeCases() {
          return [
            { name: 'wrong PAX commit comment', create: wrongComment },
            { name: 'repeated global PAX metadata', create: repeatedMetadata },
            { name: 'non-leading global PAX metadata', create: nonLeadingMetadata },
            { name: 'unknown global PAX metadata', create: unknownMetadata },
            { name: 'PAX path override metadata', create: pathOverride },
            { name: 'PAX size override metadata', create: sizeOverride }
          ];
        }
        function validatePaxSource(archive) {
          const entry = archive.subarray(0, 1024);
          const comment = Buffer.from('52 comment=7784b6b46b98ce994dd98070dc1f58fe4e506b91\n');
          if (!entry.subarray(0, 100).includes(Buffer.from('pax_global_header')) || entry[156] !== 103
            || !archive.subarray(512, 564).equals(comment)) throw new Error('The genuine leading PAX source entry changed.');
        }
        function wrongComment(archive) {
          const changed = Buffer.from(archive);
          Buffer.from('a'.repeat(40)).copy(changed, 512 + 11);
          return changed;
        }
        function repeatedMetadata(archive) {
          const entry = archive.subarray(0, 1024);
          return Buffer.concat([entry, entry, archive.subarray(1024)]);
        }
        function nonLeadingMetadata(archive) {
          const offset = firstEntryEnd(archive);
          return Buffer.concat([archive.subarray(0, offset), archive.subarray(0, 1024), archive.subarray(offset)]);
        }
        function unknownMetadata(archive) { return replacePax(archive, 'mystery', 'value'); }
        function pathOverride(archive) { return replacePax(archive, 'path', 'README.md'); }
        function sizeOverride(archive) { return replacePax(archive, 'size', '1'); }
        function replacePax(archive, key, value) {
          return Buffer.concat([paxEntry(archive, paxRecord(key, value)), archive.subarray(1024)]);
        }
        async function verifyCase(testCase, original, manifest, inventory, producer) {
          const directory = path.join(root, testCase.name.replaceAll(' ', '-'));
          await mkdir(directory, { mode: 0o700 });
          const receipt = JSON.parse(JSON.stringify(original));
          receipt.imageSource.archiveSha256 = sha256(testCase.bytes);
          receipt.imageSource.archiveBytes = testCase.bytes.length;
          const receiptBytes = Buffer.from(JSON.stringify(receipt));
          const manifestPath = path.join(directory, 'prior-server-manifest.json');
          const receiptPath = path.join(directory, 'prior-server-image-receipt.json');
          const inventoryPath = path.join(directory, 'prior-server-source-inventory.json');
          const archivePath = path.join(directory, 'prior-server-source.tar');
          await writeFile(receiptPath, receiptBytes, { mode: 0o600 });
          await writeFile(manifestPath, manifest, { mode: 0o600 });
          await writeFile(inventoryPath, inventory, { mode: 0o600 });
          await writeFile(archivePath, testCase.bytes, { mode: 0o600 });
          const archiveBefore = await lstat(archivePath);
          let accepted = false;
          try { await proof.verifyNative5ServerProof(receiptPath, producer, receipt.image.reference); accepted = true; } catch { }
          const preserved = (await readFile(receiptPath)).equals(receiptBytes) && (await readFile(manifestPath)).equals(manifest)
            && (await readFile(inventoryPath)).equals(inventory) && (await lstat(archivePath)).ino === archiveBefore.ino
            && await fileSha(archivePath) === sha256(testCase.bytes);
          await rm(directory, { recursive: true, force: false });
          return { Name: testCase.name, Accepted: accepted, Preserved: preserved, ExpectedReference: false };
        }
        function firstEntryEnd(archive) {
          const header = archive.subarray(1024, 1536);
          const size = Number.parseInt(header.toString('ascii', 124, 136).replace(/[\0 ]+$/g, ''), 8);
          return 1024 + 512 + Math.ceil(size / 512) * 512;
        }
        function paxRecord(key, value) {
          const body = `${key}=${value}\n`;
          let length = Buffer.byteLength(body) + 2;
          while (length !== Buffer.byteLength(`${length} ${body}`)) length = Buffer.byteLength(`${length} ${body}`);
          return Buffer.from(`${length} ${body}`);
        }
        function paxEntry(archive, payload) {
          const header = Buffer.from(archive.subarray(0, 512));
          header.fill(0, 124, 136);
          header.write(payload.length.toString(8).padStart(11, '0') + '\0', 124, 'ascii');
          header.fill(32, 148, 156);
          const checksum = header.reduce((sum, value) => sum + value, 0);
          header.write(checksum.toString(8).padStart(6, '0') + '\0 ', 148, 'ascii');
          const padding = Buffer.alloc(Math.ceil(payload.length / 512) * 512);
          payload.copy(padding);
          return Buffer.concat([header, padding]);
        }
        function sha256(bytes) { return createHash('sha256').update(bytes).digest('hex'); }
        async function fileSha(file) {
          const hash = createHash('sha256');
          for await (const chunk of createReadStream(file)) hash.update(chunk);
          return hash.digest('hex');
        }
        """;

    internal static async Task<IReadOnlyList<Native5ServerSourceVerifierResult>> ProbeAsync(
        Native5ServerParserCase parserCase)
    {
        var payload = JsonSerializer.Serialize(new
        {
            receipt = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(parserCase.Receipt)),
            manifest = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(parserCase.Manifest))
        });
        var result = await IsolatedAggregateNodeProcess.RunAsync(
            ["--input-type=module", "-e", Probe, ProofModulePath(), SourceModulePath(), payload],
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Error);
        await Assert.That(result.Error).IsEmpty();
        return JsonSerializer.Deserialize<Native5ServerSourceVerifierResult[]>(result.Output)
            ?? throw new InvalidOperationException(ProcessFailure);
    }

    private static string ProofModulePath()
        => Path.Combine(IsolatedAggregateNodeProcess.RepositoryRoot(), "scripts", "Features", "StorageRecovery",
            "native5-server-proof.mjs");

    private static string SourceModulePath()
        => Path.Combine(IsolatedAggregateNodeProcess.RepositoryRoot(), "scripts", "Features", "StorageRecovery",
            "native5-server-source.mjs");

    private const string ProcessFailure = "The native5 PAX-verifier child returned an invalid response.";
}
