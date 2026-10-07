using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal sealed class LocalRf3ImageContextSnapshotTests
{
    private const string SuccessOutput = "local-image-snapshot-ok";
    private const string Program = """
        import assert from 'node:assert/strict';
        import { chmod, lstat, mkdir, readFile, rm, symlink, writeFile } from 'node:fs/promises';
        import path from 'node:path';
        import { pathToFileURL } from 'node:url';
        const rootPath = process.argv[1];
        const moduleUrl = pathToFileURL(rootPath);
        const { cleanupBuildContextSnapshot, createBuildContextSnapshot, readBuildInputs } =
          await import(moduleUrl);
        const { localImage } = await import(new URL('./local-image-contracts.mjs', moduleUrl));
        const parent = process.argv[2];
        const root = path.join(parent, 'checkout');
        const markerRelative = 'src/KeyLoad.Core/marker.txt';
        const markerBytes = Buffer.from('original admitted source bytes');
        const updatedBytes = Buffer.from('changed checkout bytes after snapshot');
        const markerPath = path.join(root, markerRelative);
        const outsidePath = path.join(parent, 'outside.txt');
        let firstSnapshot;
        let secondSnapshot;
        let primary;
        try {
          await createContext(root, markerBytes);
          await writeFile(outsidePath, 'outside remains untouched');
          const originalOutside = await readFile(outsidePath);
          await symlink(outsidePath, path.join(root, 'src', 'KeyLoad.Core', 'untrusted-link'));
          let rejected = false;
          try { await createBuildContextSnapshot(root); }
          catch (error) { rejected = error?.message === 'LocalRf3ImageContextInvalid'; }
          assert.equal(rejected, true, 'The real source-tree symlink must be rejected.');
          assert.deepEqual(await readFile(outsidePath), originalOutside);
          await assertMissing(path.join(root, 'TestResults'));
          await rm(path.join(root, 'src', 'KeyLoad.Core', 'untrusted-link'));
          const overflowDirectory = path.join(root, 'src', 'KeyLoad.Core', 'admission-overflow');
          await mkdir(overflowDirectory);
          for (let index = 0; index <= localImage.maxInputFiles; index++) {
            await writeFile(path.join(overflowDirectory, index + '.txt'), 'x');
          }
          let overLimitRejected = false;
          try { await createBuildContextSnapshot(root); }
          catch (error) { overLimitRejected = error?.message === 'LocalRf3ImageContextInvalid'; }
          assert.equal(overLimitRejected, true, 'The bounded native source walk must reject excess entries.');
          assert.deepEqual(await readFile(outsidePath), originalOutside);
          assert.deepEqual(await readFile(markerPath), markerBytes);
          await assertMissing(path.join(root, 'TestResults'));
          await rm(overflowDirectory, { recursive: true });
          const before = await readBuildInputs(root);
          firstSnapshot = await createBuildContextSnapshot(root);
          assert.equal(firstSnapshot.digest, before.digest);
          assert.equal(firstSnapshot.files, before.files);
          assert.equal(firstSnapshot.bytes, before.bytes);
          const snapshotMarker = path.join(firstSnapshot.directory, markerRelative);
          assert.deepEqual(await readFile(snapshotMarker), markerBytes);
          assert.equal(await readFile(path.join(firstSnapshot.directory,
            'tests/KeyLoad.UnitTests/Features/CodeQuality/Build/FunctionalCompilationIdentity.targets'), 'utf8'), 'identity');
          const buildScaffold = firstSnapshot.scaffolds.find(entry =>
            entry.relative === 'tests/KeyLoad.UnitTests/Features/CodeQuality/Build');
          assert.ok(buildScaffold, 'The standalone COPY parent chain must be captured as scaffolding.');
          if (process.platform !== 'win32') {
            assert.equal((await lstat(snapshotMarker)).mode & 0o777, 0o640);
            assert.equal((await lstat(path.join(firstSnapshot.directory, 'src', 'KeyLoad.Core'))).mode & 0o777, 0o750);
            assert.equal((await lstat(path.join(firstSnapshot.directory, buildScaffold.relative))).mode & 0o777,
              buildScaffold.mode);
          }
          await writeFile(markerPath, updatedBytes);
          await chmod(markerPath, 0o600);
          assert.deepEqual(await readFile(snapshotMarker), markerBytes);
          const changed = await readBuildInputs(root);
          assert.notEqual(changed.digest, firstSnapshot.digest);
          const firstDirectory = firstSnapshot.directory;
          await cleanupBuildContextSnapshot(firstSnapshot);
          firstSnapshot = null;
          await assertMissing(firstDirectory);
          secondSnapshot = await createBuildContextSnapshot(root);
          assert.deepEqual(await readFile(path.join(secondSnapshot.directory, markerRelative)), updatedBytes);
          if (process.platform !== 'win32') {
            assert.equal((await lstat(path.join(secondSnapshot.directory, markerRelative))).mode & 0o777, 0o600);
          }
          await cleanupBuildContextSnapshot(secondSnapshot);
          secondSnapshot = null;
        } catch (error) {
          primary = error;
        }
        const cleanupFailures = [];
        if (firstSnapshot) await captureCleanup(() => cleanupBuildContextSnapshot(firstSnapshot), cleanupFailures);
        if (secondSnapshot) await captureCleanup(() => cleanupBuildContextSnapshot(secondSnapshot), cleanupFailures);
        await captureCleanup(() => rm(parent, { recursive: true, force: true }), cleanupFailures);
        const failures = primary ? [primary, ...cleanupFailures] : cleanupFailures;
        if (failures.length === 1) throw failures[0];
        if (failures.length > 1) throw new AggregateError(failures, 'Local image snapshot scenario and cleanup failed.');
        console.log('local-image-snapshot-ok');
        async function captureCleanup(action, failures) {
          try { await action(); }
          catch (error) { failures.push(error); }
        }
        async function assertMissing(directory) {
          await assert.rejects(lstat(directory), error => error?.code === 'ENOENT');
        }
        async function createContext(rootDirectory, marker) {
          const src = path.join(rootDirectory, 'src', 'KeyLoad.Core');
          await mkdir(src, { recursive: true });
          await chmod(path.join(rootDirectory, 'src'), 0o750);
          await chmod(src, 0o750);
          const dockerfile = [
            'FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:' + 'a'.repeat(64) + ' AS build',
            'COPY global.json Directory.Build.props Directory.Build.targets Directory.Packages.props NuGet.Config .editorconfig LICENSE ./',
            'COPY KeyLoad.slnx ./KeyLoad.slnx',
            'COPY tests/KeyLoad.UnitTests/Features/CodeQuality/Build/FunctionalCompilationIdentity.targets ./tests/KeyLoad.UnitTests/Features/CodeQuality/Build/FunctionalCompilationIdentity.targets',
            'COPY src/ ./src/',
            'RUN dotnet publish KeyLoad.slnx',
            'FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:' + 'b'.repeat(64),
            'COPY --from=build /app/publish/ ./',
          ].join('\n') + '\n';
          await writeFile(path.join(rootDirectory, 'Dockerfile'), dockerfile);
          await writeFile(path.join(rootDirectory, '.dockerignore'), '');
          for (const name of ['global.json', 'Directory.Build.props', 'Directory.Build.targets',
            'Directory.Packages.props', 'NuGet.Config', '.editorconfig', 'LICENSE', 'KeyLoad.slnx']) {
            await writeFile(path.join(rootDirectory, name), name);
          }
          const identity = path.join(rootDirectory, 'tests', 'KeyLoad.UnitTests', 'Features', 'CodeQuality',
            'Build', 'FunctionalCompilationIdentity.targets');
          await mkdir(path.dirname(identity), { recursive: true });
          await writeFile(identity, 'identity');
          const payload = path.join(src, 'marker.txt');
          await writeFile(payload, marker);
          await chmod(payload, 0o640);
        }
        """;

    [Test]
    public async Task AcTest015LocalImageBuildUsesImmutableAdmittedFilesystemSnapshotAndCleansItBeforeReuse()
    {
        var root = FindRepositoryRoot();
        var modulePath = Path.Combine(root, "scripts", "Features", "TestInfrastructure", "local-image-snapshot.mjs");
        var ownedRoot = Path.Combine(Path.GetTempPath(), "keyload-local-image-context-" + Guid.NewGuid().ToString("N"));
        var failures = new List<Exception>();
        var rootCreated = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            if (Directory.Exists(ownedRoot) || File.Exists(ownedRoot))
            { throw new IOException("The unique local image filesystem root is already occupied."); }
            Directory.CreateDirectory(ownedRoot);
            rootCreated = true;
            if (!OperatingSystem.IsWindows())
            { File.SetUnixFileMode(ownedRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
            var result = await LocalImageSnapshotProcess.RunAsync(root, modulePath, Program, ownedRoot,
                TestContext.Current!.Execution.CancellationToken).ConfigureAwait(false);
            await Assert.That(result.ExitCode).IsEqualTo(0);
            await Assert.That(result.StandardOutput.Trim()).IsEqualTo(SuccessOutput);
            await Assert.That(result.StandardError).IsEmpty();
        }, failures).ConfigureAwait(false);
        if (rootCreated && Directory.Exists(ownedRoot))
        { ServerFailureObserver.Observe(() => Directory.Delete(ownedRoot, recursive: true), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "KeyLoad.slnx")))
        { directory = directory.Parent; }
        return directory?.FullName ?? throw new DirectoryNotFoundException("Local image snapshot regression requires the checkout.");
    }
}
