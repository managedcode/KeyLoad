namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class SampleChunkBenchmarkRejectionProgram
{
    internal static string Source => Prefix + SampleChunkBenchmarkRejectionMutations.Source;

    private const string Prefix = """
        import assert from 'node:assert/strict';
        import { createHash } from 'node:crypto';
        import { constants } from 'node:fs';
        import { mkdir, open, lstat } from 'node:fs/promises';
        import path from 'node:path';

        const MAX_FILE = 64 * 1024 * 1024;
        const MAX_TOTAL = 512 * 1024 * 1024;
        const MAX_REPORTS = 36;
        const EXPECTED_CASES = 36;
        const EXPECTED_MANIFESTS = 9;
        const evidence = path.resolve(process.argv[2]);
        const reportsDirectory = path.join(evidence, 'bdn');
        const corpusDirectory = path.join(evidence, 'corpus');
        const copiesRoot = path.join(evidence, 'rejection-oracles');
        const copyReportsDirectory = path.join(copiesRoot, 'bdn');
        const copyCorpusDirectory = path.join(copiesRoot, 'corpus');
        const reportModule = await import(process.argv[1]);
        const inventoryModule = await import(new URL('./sample-chunk-development-inventory.mjs', process.argv[1]));

        await main();

        async function main() {
          const originals = await loadOriginals();
          const evidenceReceipt = originals.metadata.receipt;
          const validated = await validateOriginals(originals);
          await validateReceipt(evidenceReceipt, originals.metadata, validated);
          await createCopies(validated.reports.reportFiles, validated.manifests.files);
          await validateCopies(originals.source, validated.reports.hostEnvironment);
          await rejectReportMutations(validated.reports.reportFiles[0].path);
          await rejectManifestMutations(validated.manifests.files[0].path, originals.source,
            validated.reports);
          await assertOriginalsUnchanged(originals);
          await validateCopies(originals.source, validated.reports.hostEnvironment);
          const revalidated = await validateOriginals(originals);
          await validateReceipt(evidenceReceipt, originals.metadata, revalidated);
          process.stdout.write('Validated the real Dry receipt and rejected all copied parser mutations.\n');
        }

        async function loadOriginals() {
          const metadataNames = ['source-before.json', 'source-after.json', 'release-before.json',
            'release-after.json', 'receipt.json'];
          const metadata = {};
          const metadataHashes = new Map();
          let total = 0;
          for (const name of metadataNames) {
            const bytes = await inventoryModule.readBoundedRegularFile(evidence, name, MAX_FILE);
            total += bytes.length;
            metadata[name.replace('.json', '').replaceAll('-', '')] = JSON.parse(bytes.toString('utf8'));
            metadataHashes.set(name, { sha256: digest(bytes), bytes: bytes.length });
          }
          const source = metadata.sourcebefore;
          const bdn = await reportModule.validateBenchmarkReports(reportsDirectory, 'dry');
          const corpus = await reportModule.validateCorpusManifests(corpusDirectory, source.head,
            source.sha256, bdn.hostEnvironment);
          checkBounds(bdn.reportFiles, corpus.files, total);
          return { metadata, metadataHashes, source, reports: bdn.reportFiles, manifests: corpus.files };
        }

        async function validateOriginals(originals) {
          const reports = await reportModule.validateBenchmarkReports(reportsDirectory, 'dry');
          const manifests = await reportModule.validateCorpusManifests(corpusDirectory,
            originals.source.head, originals.source.sha256, reports.hostEnvironment);
          checkBounds(reports.reportFiles, manifests.files, metadataBytes(originals.metadataHashes));
          return { reports, manifests };
        }

        async function validateReceipt(receipt, metadata, validated) {
          const sourceBefore = metadata.sourcebefore;
          const sourceAfter = metadata.sourceafter;
          const binariesBefore = metadata.releasebefore;
          const binariesAfter = metadata.releaseafter;
          assert.equal(receipt.schemaVersion, 1);
          assert.equal(receipt.scope, 'local_sample_chunk_codec_development_control_only');
          assert.equal(receipt.runMode, 'dry');
          assert.equal(receipt.childExitCode, 0);
          assert.equal(receipt.unchangedAfterChild, true);
          assert.equal(receipt.sourceHead, sourceBefore.head);
          assert.equal(receipt.sourceInventorySha256, sourceBefore.sha256);
          assert.equal(receipt.releaseBinaryInventorySha256, binariesBefore.sha256);
          assert.equal(sourceBefore.head, sourceAfter.head);
          assert.equal(sourceBefore.sha256, sourceAfter.sha256);
          assert.equal(binariesBefore.sha256, binariesAfter.sha256);
          assert.equal(sourceBefore.fileCount, sourceAfter.fileCount);
          assert.equal(sourceBefore.totalBytes, sourceAfter.totalBytes);
          assert.equal(binariesBefore.fileCount, binariesAfter.fileCount);
          assert.equal(binariesBefore.totalBytes, binariesAfter.totalBytes);
          assert.equal(receipt.caseCount, EXPECTED_CASES);
          assert.equal(receipt.corpusManifestCount, EXPECTED_MANIFESTS);
          for (const [key, expected] of Object.entries({ localDevelopmentOnly: true, performanceQualified: false,
            dryExecutionOnly: true, githubQualified: false, databaseScaleEvidence: false,
            canonicalChunkStorage: false, rewriteCostQualified: false, correctionRecoveryQualified: false })) {
            assert.equal(receipt[key], expected, `Invalid receipt flag ${key}.`);
          }
          assert.deepEqual(receipt.requestedSettings,
            { launchCount: 1, warmupCount: 0, iterationCount: 1, iterationTimeMilliseconds: 1, outliers: 'DontRemove' });
          const expectedArguments = [inventoryModule.benchmarkAssembly, '--exporters', 'fulljson', '--filter',
            '*SampleChunkSerializationBenchmarks*', '--keepFiles', '--stopOnFirstError', '--outliers', 'DontRemove',
            '--job', 'Dry', '--launchCount', '1', '--warmupCount', '0', '--iterationCount', '1',
            '--iterationTime', '1', '--artifacts', reportsDirectory];
          assert.deepEqual(receipt.benchmarkArguments, expectedArguments);
          assert.deepEqual(receipt.reportFiles, validated.reports.reportFiles);
          assert.deepEqual(receipt.corpusManifests, validated.manifests.files);
          assert.equal(receipt.cases.length, EXPECTED_CASES);
          assert.equal(receipt.sourceInventoryFile, 'source-before.json');
          assert.equal(receipt.releaseBinaryInventoryFile, 'release-before.json');
          assert.deepEqual(receipt.manifestEnvironment, {
            directory: corpusDirectory,
            sourceHead: sourceBefore.head,
            sourceInventorySha256: sourceBefore.sha256
          });
          assert.equal(receipt.benchmarkDotNetVersion, validated.reports.hostEnvironment.BenchmarkDotNetVersion);
          assert.deepEqual(receipt.hostEnvironment, validated.reports.hostEnvironment);
          assert.equal(receipt.cases.length, validated.reports.cases.length);
          assert.deepEqual(receipt.cases, validated.reports.cases);
          await validateLog(receipt.standardOutput, 'benchmark.stdout.log');
          await validateLog(receipt.standardError, 'benchmark.stderr.log');
        }

        function checkBounds(reports, manifests, metadataTotal) {
          assert.ok(reports.length > 0 && reports.length <= MAX_REPORTS);
          assert.equal(manifests.length, EXPECTED_MANIFESTS);
          let total = metadataTotal;
          for (const file of [...reports, ...manifests]) {
            assert.ok(Number.isSafeInteger(file.bytes) && file.bytes >= 0 && file.bytes <= MAX_FILE);
            total += file.bytes;
          }
          assert.ok(total <= MAX_TOTAL);
        }

        function metadataBytes(hashes) {
          return [...hashes.values()].reduce((sum, item) => sum + item.bytes, 0);
        }

        async function validateCopies(source, hostEnvironment) {
          const reports = await reportModule.validateBenchmarkReports(copyReportsDirectory, 'dry');
          const manifests = await reportModule.validateCorpusManifests(copyCorpusDirectory,
            source.head, source.sha256, hostEnvironment);
          assert.deepEqual(reports.hostEnvironment, hostEnvironment);
          assert.equal(reports.cases.length, EXPECTED_CASES);
          assert.equal(manifests.files.length, EXPECTED_MANIFESTS);
        }

        async function validateLog(log, expectedPath) {
          assert.equal(log.path, expectedPath);
          assert.ok(Number.isSafeInteger(log.bytes) && log.bytes >= 0 && log.bytes <= 16 * 1024 * 1024);
          assert.match(log.sha256, /^[0-9a-f]{64}$/);
          assert.equal(log.truncated, false);
          const bytes = await inventoryModule.readBoundedRegularFile(evidence, log.path, 16 * 1024 * 1024);
          assert.equal(bytes.length, log.bytes);
          assert.equal(digest(bytes), log.sha256);
        }
        """;
}
