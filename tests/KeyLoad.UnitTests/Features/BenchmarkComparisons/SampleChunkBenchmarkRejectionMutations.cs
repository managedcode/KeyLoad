namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class SampleChunkBenchmarkRejectionMutations
{
    internal const string Source = """
        async function createCopies(reports, manifests) {
          await mkdir(copiesRoot, { mode: 0o700 });
          await mkdir(copyReportsDirectory, { mode: 0o700 });
          await mkdir(copyCorpusDirectory, { mode: 0o700 });
          for (const file of reports) await copyFile(reportsDirectory, copyReportsDirectory, file.path);
          for (const file of manifests) await copyFile(corpusDirectory, copyCorpusDirectory, file.path);
        }

        async function rejectReportMutations(reportPath) {
          const mutations = [
            ['missing case', 'Expected exactly 36 unique chunk benchmark cases', item => item.Benchmarks.pop()],
            ['duplicate case', 'A benchmark case is duplicated', item => item.Benchmarks.push({ ...item.Benchmarks[0] })],
            ['unexpected method', 'missing its frozen type, method, parameters, or measurements.', item => { firstCase(item).Method = 'UnexpectedMethod'; }],
            ['unexpected parameters', 'Unexpected chunk benchmark parameters', item => { firstCase(item).Parameters = 'RecordCount=257&Corpus=regular'; }],
            ['wrong launch settings', 'setting LaunchCount must be 1', item => replaceDisplaySetting(item, 'LaunchCount=1', 'LaunchCount=2')],
            ['nonpositive measurement', 'missing or invalid Workload/Result iterations', item => mutateWorkload(item, result => { result.Nanoseconds = 0; })],
            ['nonfinite-compatible measurement', 'missing or invalid Workload/Result iterations', item => mutateWorkload(item, result => { result.Nanoseconds = null; })],
            ['statistics corruption', 'lacks measured cost or allocation data', item => { firstCase(item).Statistics.Mean = 0; }]
          ];
          for (const [label, expectedError, mutate] of mutations) {
            await mutateAndReject(label, expectedError, 'bdn', reportPath, mutate,
              () => reportModule.validateBenchmarkReports(copyReportsDirectory, 'dry'));
          }
        }

        async function rejectManifestMutations(manifestPath, source, reports) {
          const mutations = [
            ['source head mismatch', 'has invalid sourceHead', item => { item.sourceHead = differentHex(source.head); }],
            ['source inventory mismatch', 'has invalid sourceInventorySha256', item => { item.sourceInventorySha256 = differentHex(source.sha256); }],
            ['database scale claim', 'has invalid databaseScaleEvidence', item => { item.databaseScaleEvidence = true; }],
            ['GitHub qualification claim', 'has invalid githubQualified', item => { item.githubQualified = true; }]
          ];
          for (const [label, expectedError, mutate] of mutations) {
            await mutateAndReject(label, expectedError, 'corpus', manifestPath, mutate,
              () => reportModule.validateCorpusManifests(copyCorpusDirectory, source.head,
                source.sha256, reports.hostEnvironment));
          }
        }

        async function mutateAndReject(label, expectedError, kind, filePath, mutate, validate) {
          await validate();
          const sourceRoot = kind === 'bdn' ? reportsDirectory : corpusDirectory;
          const copyRoot = kind === 'bdn' ? copyReportsDirectory : copyCorpusDirectory;
          await copyFile(sourceRoot, copyRoot, filePath);
          const file = path.join(copyRoot, ...safeParts(filePath));
          const info = await lstat(file);
          assert.ok(info.isFile() && !info.isSymbolicLink());
          const original = await inventoryModule.readBoundedRegularFile(copyRoot, filePath, MAX_FILE);
          const value = JSON.parse(original.toString('utf8'));
          mutate(value);
          await replaceOwnedCopy(file, Buffer.from(`${JSON.stringify(value)}\n`, 'utf8'));
          const changedBytes = await inventoryModule.readBoundedRegularFile(copyRoot, filePath, MAX_FILE);
          JSON.parse(changedBytes.toString('utf8'));
          try {
            await rejects(label, expectedError, validate);
          } finally {
            await copyFile(sourceRoot, copyRoot, filePath);
            await validate();
          }
        }

        async function rejects(label, expectedError, validate) {
          await assert.rejects(validate, error => {
            assert.ok(error instanceof Error);
            assert.ok(error.message.includes(expectedError),
              `Unexpected rejection for ${label}: ${error.message}`);
            return true;
          }, `The actual validator accepted copied mutation: ${label}.`);
        }

        function firstCase(report) {
          assert.ok(Array.isArray(report.Benchmarks) && report.Benchmarks.length > 0);
          return report.Benchmarks[0];
        }

        function mutateWorkload(report, mutate) {
          const target = report.Benchmarks.find(item => item.Measurements?.some(measurement =>
            measurement.IterationMode === 'Workload' && measurement.IterationStage === 'Result'));
          assert.ok(target);
          const result = target.Measurements.find(measurement => measurement.IterationMode === 'Workload'
            && measurement.IterationStage === 'Result');
          mutate(result);
        }

        function replaceDisplaySetting(report, before, after) {
          const item = firstCase(report);
          assert.ok(item.DisplayInfo.includes(before));
          item.DisplayInfo = item.DisplayInfo.replace(before, after);
        }

        async function copyFile(sourceRoot, destinationRoot, relative) {
          const bytes = await inventoryModule.readBoundedRegularFile(sourceRoot, relative, MAX_FILE);
          const destination = path.join(destinationRoot, ...safeParts(relative));
          await ensureParent(destinationRoot, path.dirname(destination));
          let create = false;
          try {
            const info = await lstat(destination);
            assert.ok(info.isFile() && !info.isSymbolicLink());
          } catch (error) {
            if (error?.code !== 'ENOENT') throw error;
            create = true;
          }
          await replaceOwnedCopy(destination, bytes, create);
        }

        async function ensureParent(root, parent) {
          const relative = path.relative(root, parent);
          assert.ok(relative !== '..' && !relative.startsWith(`..${path.sep}`) && !path.isAbsolute(relative));
          let current = root;
          for (const component of relative.split(path.sep).filter(Boolean)) {
            current = path.join(current, component);
            try { await mkdir(current, { mode: 0o700 }); }
            catch (error) { if (error?.code !== 'EEXIST') throw error; }
            const info = await lstat(current);
            assert.ok(info.isDirectory() && !info.isSymbolicLink());
          }
        }

        async function replaceOwnedCopy(file, bytes, create = false) {
          const flags = constants.O_WRONLY | (create ? constants.O_CREAT | constants.O_EXCL : constants.O_TRUNC)
            | (constants.O_NOFOLLOW ?? 0);
          const handle = await open(file, flags, 0o600);
          try { await handle.writeFile(bytes); await handle.sync(); }
          finally { await handle.close(); }
        }

        function safeParts(relative) {
          const parts = relative.split('/');
          assert.ok(relative.length > 0 && !path.isAbsolute(relative)
            && parts.every(part => part.length > 0 && part !== '.' && part !== '..' && !part.includes('\0')));
          return parts;
        }

        async function assertOriginalsUnchanged(originals) {
          for (const [name, before] of originals.metadataHashes) {
            const bytes = await inventoryModule.readBoundedRegularFile(evidence, name, MAX_FILE);
            assert.equal(digest(bytes), before.sha256, `Original metadata changed: ${name}`);
            assert.equal(bytes.length, before.bytes);
          }
          await assertFileInventory(reportsDirectory, originals.reports, name => name.endsWith('-report-full.json'));
          await assertFileInventory(corpusDirectory, originals.manifests, () => true);
        }

        async function assertFileInventory(directory, expected, filter) {
          const actual = await inventoryModule.captureOutputFiles(directory, filter);
          assert.deepEqual(actual, expected);
        }

        function differentHex(value) { return (value[0] === '0' ? '1' : '0') + value.slice(1); }
        function digest(bytes) { return createHash('sha256').update(bytes).digest('hex'); }

        """;
}
