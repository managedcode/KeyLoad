using System.Text.Json;
using KeyLoad.UnitTests.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed record Native5ServerSourceVerifierResult(string Name, bool Accepted, bool Preserved,
    bool ExpectedReference)
{
    public string Error { get; init; } = string.Empty;
}

internal static class Native5ServerSourceVerifierNodeProcess
{
    private const string Probe = """
        import { createReadStream } from 'node:fs';
        import { chmod, link, lstat, mkdir, mkdtemp, readFile, readdir, rm, rmdir, symlink, writeFile } from 'node:fs/promises';
        import os from 'node:os';
        import path from 'node:path';
        import { createHash } from 'node:crypto';
        import { pathToFileURL } from 'node:url';
        const proof = await import(pathToFileURL(process.argv[1]).href);
        const sourceBuilder = await import(pathToFileURL(process.argv[2]).href);
        const input = JSON.parse(process.argv[3]);
        const root = await mkdtemp(path.join(os.tmpdir(), 'keyload-native5-verified-source-'));
        const evidence = path.join(root, 'evidence');
        await chmod(root, 0o700);
        await mkdir(evidence, { mode: 0o700 });
        let source;
        let primary;
        try {
          source = await sourceBuilder.createNative5ServerSource({ runnerTemp: root, runId: '1', runAttempt: '1',
            workspace: process.cwd(), evidenceDirectory: evidence });
          const sourceInventory = await readFile(path.join(evidence, 'prior-server-source-inventory.json'));
          const baseReceipt = JSON.parse(Buffer.from(input.receipt, 'base64').toString('utf8'));
          const baseManifest = Buffer.from(input.manifest, 'base64');
          baseReceipt.imageSource.archiveSha256 = source.archiveSha256;
          baseReceipt.imageSource.archiveBytes = source.archiveBytes;
          baseReceipt.imageSource.inventoryFileSha256 = sha256(sourceInventory);
          const producer = baseReceipt.producer;
          const expectedReference = baseReceipt.image.reference;
          const manifestName = 'prior-server-manifest.json';
          const receiptName = 'prior-server-image-receipt.json';
          const inventoryName = 'prior-server-source-inventory.json';
          const archiveName = 'prior-server-source.tar';
          const results = [];
          const positive = await runVerifier('actual fixed source sidecars with controlled parser image input', evidence,
            baseReceipt, baseManifest, sourceInventory, false, false, producer, expectedReference,
            undefined, true);
          results.push(positive);
          const exportInventory = await sourceBuilder.verifyNative5ServerExport(source);
          results.push(await exportResult('actual prior source export matches pinned inventory', exportInventory));
          const unknownDirectory = path.join(source.workspace, '.native5-empty-directory-control');
          await mkdir(unknownDirectory, { mode: 0o700 });
          let directoryError = '';
          try { await sourceBuilder.verifyNative5ServerExport(source); } catch (error) { directoryError = error?.message ?? ''; }
          const directoryPreserved = directoryError === 'The immutable native5 source export is invalid.'
            && (await lstat(unknownDirectory)).isDirectory() && (await readdir(unknownDirectory)).length === 0
            && (await readFile(path.join(evidence, inventoryName))).equals(sourceInventory)
            && await fileSha(source.archivePath) === source.archiveSha256;
          results.push({ Name: 'unknown empty source directory rejected exactly', Accepted: directoryError.length === 0,
            Preserved: directoryPreserved, ExpectedReference: false, Error: directoryError });
          await rmdir(unknownDirectory);
          const exportAfterInventory = await sourceBuilder.verifyNative5ServerExport(source);
          results.push(await exportResult('prior source export verifies after owned directory removal', exportAfterInventory));
          results.push(await alteredCase('inventory row count mismatch', baseReceipt, baseManifest,
            Buffer.from('{"schemaVersion":1,"files":[]}'), producer, expectedReference));
          results.push(await alteredCase('inventory receipt hash mismatch', baseReceipt, baseManifest,
            sourceInventory, producer, expectedReference, receipt => { receipt.imageSource.inventoryFileSha256 = 'c'.repeat(64); }));
          results.push(await alteredCase('inventory canonical order mismatch', baseReceipt, baseManifest,
            mutateInventory(sourceInventory, files => { [files[0], files[1]] = [files[1], files[0]]; }),
            producer, expectedReference));
          results.push(await alteredCase('inventory transcript mismatch', baseReceipt, baseManifest,
            mutateInventory(sourceInventory, files => { files[0].sha256 = 'c'.repeat(64); }),
            producer, expectedReference));
          results.push(await alteredCase('archive observed digest mismatch', baseReceipt, baseManifest,
            sourceInventory, producer, expectedReference, receipt => { receipt.imageSource.archiveSha256 = 'c'.repeat(64); }));
          results.push(await alteredCase('archive observed length mismatch', baseReceipt, baseManifest,
            sourceInventory, producer, expectedReference, receipt => { receipt.imageSource.archiveBytes += 1; }));
          results.push(await alteredCase('oversized inventory sidecar', baseReceipt, baseManifest,
            Buffer.alloc(1024 * 1024 + 1), producer, expectedReference));
          results.push(await alteredCase('oversized receipt sidecar', baseReceipt, baseManifest,
            sourceInventory, producer, expectedReference, null, receipt => `${JSON.stringify(receipt)}${' '.repeat(65_537)}`));
          results.push(await alteredCase('manifest symlink preserved', baseReceipt, baseManifest,
            sourceInventory, producer, expectedReference, null, null, true));
          results.push(await alteredCase('evidence directory symlink preserved', baseReceipt, baseManifest,
            sourceInventory, producer, expectedReference, null, null, false, true));
          const archivePreserved = await fileSha(source.archivePath) === source.archiveSha256;
          for (const result of results) result.Preserved = result.Preserved && archivePreserved;
          process.stdout.write(JSON.stringify(results));
          async function exportResult(name, inventory) {
            const archiveInfo = await lstat(source.archivePath);
            const archiveUnchanged = archiveInfo.size === source.archiveBytes
              && await fileSha(source.archivePath) === source.archiveSha256;
            const exact = inventory.fileCount === 2557 && inventory.files.length === 2557
              && inventory.expandedBytes === 22520638
              && inventory.sourceInventorySha256 === '2d60112c44b55fdbcfd36bdeb14bf821e38d55e43df09c0c85f10558a5e332d0';
            return { Name: name, Accepted: exact, Preserved: archiveUnchanged,
              ExpectedReference: false, Error: '' };
          }
          async function alteredCase(name, receipt, manifest, inventory, expectedProducer, reference,
            alter = null, serialize = null, manifestLink = false, directoryLink = false) {
            const changed = JSON.parse(JSON.stringify(receipt));
            changed.imageSource.inventoryFileSha256 = sha256(inventory);
            if (alter) alter(changed);
            const receiptBytes = Buffer.from(serialize ? serialize(changed) : JSON.stringify(changed));
            return runVerifier(name, path.join(root, name.replaceAll(' ', '-')), changed, manifest,
              inventory, manifestLink, directoryLink, expectedProducer, reference, receiptBytes);
          }
          async function runVerifier(name, directory, receipt, manifest, inventory, manifestLink,
            directoryLink, expectedProducer, reference, receiptBytes = Buffer.from(JSON.stringify(receipt)),
            useExistingArchive = false) {
            const target = directoryLink ? path.join(root, `${path.basename(directory)}-target`) : directory;
            await mkdir(target, { recursive: true, mode: 0o700 });
            if (directoryLink) await symlink(target, directory, 'dir');
            const receiptPath = path.join(directory, receiptName);
            const manifestPath = path.join(directory, manifestName);
            const inventoryPath = path.join(directory, inventoryName);
            const archivePath = path.join(directory, archiveName);
            await writeFile(receiptPath, receiptBytes, { mode: 0o600 });
            if (manifestLink) {
              const linkedManifest = path.join(directory, 'manifest-target.json');
              await writeFile(linkedManifest, manifest, { mode: 0o600 });
              await symlink(linkedManifest, manifestPath);
            } else await writeFile(manifestPath, manifest, { mode: 0o600 });
            await writeFile(inventoryPath, inventory, { mode: 0o600 });
            if (!useExistingArchive) await link(source.archivePath, archivePath);
            const before = await lstat(archivePath);
            let accepted = false;
            let actualReference = '';
            try {
              const result = await proof.verifyNative5ServerProof(receiptPath, expectedProducer, reference);
              accepted = true;
              actualReference = result.reference;
            } catch { }
            const manifestBytes = await readFile(manifestLink ? path.join(directory, 'manifest-target.json') : manifestPath);
            const preserved = (await readFile(receiptPath)).equals(receiptBytes)
              && manifestBytes.equals(manifest)
              && (await readFile(inventoryPath)).equals(inventory)
              && (await lstat(archivePath)).ino === before.ino
              && (await lstat(manifestPath)).isSymbolicLink() === manifestLink
              && (!directoryLink || (await lstat(directory)).isSymbolicLink());
            return { Name: name, Accepted: accepted, Preserved: preserved,
              ExpectedReference: accepted && actualReference === reference };
          }
          function mutateInventory(bytes, mutate) {
            const inventory = JSON.parse(bytes.toString('utf8'));
            mutate(inventory.files);
            return Buffer.from(JSON.stringify(inventory));
          }
          function sha256(bytes) { return createHash('sha256').update(bytes).digest('hex'); }
          async function fileSha(file) {
            const hash = createHash('sha256');
            for await (const chunk of createReadStream(file)) hash.update(chunk);
            return hash.digest('hex');
          }
        } catch (error) {
          if (error?.preserveOwnedPaths === true) throw error;
          primary = error;
        }
        const cleanup = [];
        if (source) { try { await sourceBuilder.cleanupNative5ServerSource(source); } catch (error) { cleanup.push(error); } }
        try { await rm(root, { recursive: true, force: true }); } catch (error) { cleanup.push(error); }
        if (primary && cleanup.length) throw new AggregateError([primary, ...cleanup], 'Source proof and owned cleanup failed.');
        if (primary) throw primary;
        if (cleanup.length) throw new AggregateError(cleanup, 'Owned source proof cleanup failed.');
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

    private const string ProcessFailure = "The native5 source-verifier child returned an invalid response.";
}
