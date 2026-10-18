namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Controlled admission inputs prove rejection policy, never provider publication authority.</summary>
internal static class CompiledBenchmarkRuntimeNodeProgram
{
    internal const string Contract = """
        import { pathToFileURL } from 'node:url';
        const { allowedRuntimePath, runtimeBundle, validateRuntimeInventory, validateRuntimeDescriptor } =
          await import(pathToFileURL(process.argv[1]).href);
        const assert = (value) => { if (!value) throw new Error('runtime-contract-rejected'); };
        const context = { sourceSha: 'a'.repeat(40), runId: '100', runAttempt: '2',
          repository: 'managedcode/KeyLoad', ref: 'refs/heads/main', workspace: '/home/runner/work/KeyLoad/KeyLoad' };
        const nativeRoot = runtimeBundle.packages + '/aspire.cli.linux-' + process.arch
          + '/13.6.1/tools/net10.0/linux-' + process.arch + '/versions/13.6.1_controlled/';
        const inventory = runtimeBundle.assemblies.flatMap(([role, stem]) => ['.dll', '.pdb'].map(extension =>
          ({ path: stem + extension, bytes: 128, mode: 420, sha256: 'b'.repeat(64) })))
          .concat(['dcp/dcp', 'dashboard/Aspire.Dashboard'].map(name =>
            ({ path: nativeRoot + name, bytes: 128, mode: 493, sha256: 'b'.repeat(64) })))
          .sort((left, right) => left.path < right.path ? -1 : left.path > right.path ? 1 : 0);
        const descriptor = { schemaVersion: 1, kind: runtimeBundle.kind, sourceRevision: context.sourceSha,
          runId: 100, attempt: 2, repository: context.repository, ref: context.ref, workspace: context.workspace,
          platform: 'linux', architecture: process.arch, sdkVersion: '10.0.401',
          archive: runtimeBundle.archive, archiveSha256: 'c'.repeat(64), archiveBytes: 256,
          inventory: runtimeBundle.inventory, inventorySha256: 'd'.repeat(64), fileCount: inventory.length,
          aspire: { version: '13.6.1', dcpPath: nativeRoot + 'dcp/dcp', dashboardPath: nativeRoot + 'dashboard/Aspire.Dashboard' },
          assemblies: runtimeBundle.assemblies.map(([role, stem]) => ({ role, path: stem + '.dll',
            sha256: 'b'.repeat(64), pdbPath: stem + '.pdb', pdbSha256: 'b'.repeat(64) })) };
        const healthy = () => { validateRuntimeInventory(inventory); validateRuntimeDescriptor(descriptor, context, inventory); };
        healthy();
        const variant = process.argv[2];
        const badFiles = structuredClone(inventory);
        const badDescriptor = structuredClone(descriptor);
        if (variant !== 'valid') {
          if (variant === 'source-path') badFiles[0].path = 'build/KeyLoad.AppHost/Program.cs';
          if (variant === 'traversal') badFiles[0].path = 'build/KeyLoad.AppHost/obj/../../../Program.cs';
          if (variant === 'backslash') badFiles[0].path = 'src\\KeyLoad.AppHost\\obj\\Program.cs';
          if (variant === 'duplicate') badFiles.push(structuredClone(badFiles[0]));
          if (variant === 'oversized') badFiles[0].bytes = runtimeBundle.maximumFileBytes + 1;
          if (variant === 'foreign-source') badDescriptor.sourceRevision = 'e'.repeat(40);
          if (variant === 'foreign-attempt') badDescriptor.attempt = 3;
          if (variant === 'foreign-workspace') badDescriptor.workspace += '-foreign';
          if (variant === 'foreign-dll') badDescriptor.assemblies[0].sha256 = 'e'.repeat(64);
          if (variant === 'foreign-pdb') badDescriptor.assemblies[0].pdbSha256 = 'e'.repeat(64);
          if (variant === 'foreign-native') badDescriptor.aspire.dcpPath += '-foreign';
          if (variant === 'missing-native') badFiles.splice(badFiles.findIndex(entry => entry.path.endsWith('/dcp/dcp')), 1);
          let rejected = false;
          try { validateRuntimeInventory(badFiles); validateRuntimeDescriptor(badDescriptor, context, badFiles); }
          catch { rejected = true; }
          assert(rejected);
        }
        assert(!allowedRuntimePath('README.md') && !allowedRuntimePath('/etc/passwd'));
        healthy();
        process.stdout.write('runtime-contract-accepted\n');
        """;
}
