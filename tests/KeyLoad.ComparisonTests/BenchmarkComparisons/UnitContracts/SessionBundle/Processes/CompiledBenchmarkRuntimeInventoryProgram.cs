namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Exercises original filesystem link policy using isolated repository-owned control files.</summary>
internal static class CompiledBenchmarkRuntimeInventoryProgram
{
    internal const string Program = """
        import { randomUUID } from 'node:crypto';
        import { mkdir, writeFile, symlink, rm } from 'node:fs/promises';
        import path from 'node:path';
        import { pathToFileURL } from 'node:url';
        const { collectRuntimeFiles } = await import(pathToFileURL(process.argv[1]).href);
        const root = path.join(process.argv[2], 'artifacts/comparisons/runtime-inventory', randomUUID());
        const variant = process.argv[3];
        const packageRoot = path.join(root, 'artifacts/benchmark-runtime/packages');
        const tools = path.join(packageRoot, 'aspire.cli.linux-' + process.arch,
          '13.6.1/tools/net10.0/linux-' + process.arch);
        const version = path.join(tools, 'versions/13.6.1_controlled');
        try {
          await mkdir(path.join(version, 'dcp'), { recursive: true });
          await mkdir(path.join(version, 'dashboard'));
          await writeFile(path.join(version, 'dcp/dcp'), 'execution-control', { mode: 493, flag: 'wx' });
          await writeFile(path.join(version, 'dashboard/Aspire.Dashboard'), 'execution-control', { mode: 493, flag: 'wx' });
          let target = 'versions/13.6.1_controlled';
          if (variant === 'foreign-target') {
            await mkdir(path.join(root, 'foreign'));
            target = path.join(root, 'foreign');
          }
          if (variant === 'stale-version') {
            await mkdir(path.join(tools, 'versions/13.6.0_controlled'));
            target = 'versions/13.6.0_controlled';
          }
          await symlink(target, path.join(tools, 'bundle'), 'dir');
          if (variant === 'arbitrary-link') await symlink(version, path.join(packageRoot, 'arbitrary'), 'dir');
          let files;
          let rejected = false;
          try { files = await collectRuntimeFiles(root, [], '13.6.1'); }
          catch { rejected = true; }
          if (variant === 'valid') {
            if (rejected || files.length !== 2 || files.some(file => file.path.includes('/bundle/')))
              throw new Error('runtime-inventory-control-rejected');
          } else if (!rejected) throw new Error('runtime-inventory-corruption-admitted');
          process.stdout.write('runtime-inventory-accepted\n');
        } finally { await rm(root, { recursive: true, force: true }); }
        """;
}
