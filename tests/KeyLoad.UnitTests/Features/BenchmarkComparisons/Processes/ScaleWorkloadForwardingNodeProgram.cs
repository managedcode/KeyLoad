namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class ScaleWorkloadForwardingNodeProgram
{
    internal const string Source = """
        import { pathToFileURL } from 'node:url';
        const module = await import(pathToFileURL(process.argv[1]));
        const profile = process.argv[2];
        if (profile === 'control') {
          const selected = module.selectedScaleProfile({ Benchmarks__EvidenceProfile: 'intensive-1k-c16' });
          if (selected !== undefined) process.exitCode = 1;
          const args = module.workloadArguments(selected);
          if (!args.includes('--KeyLoadTests:TimeoutMinutes=60') || args.some(value => value.startsWith('--KeyLoadTests:ScaleProfile='))) process.exitCode = 1;
        } else if (profile === 'scale') {
          const selected = module.selectedScaleProfile({ KEYLOAD_SCALE_PROFILE: 'scaled-1m-c16', Benchmarks__EvidenceProfile: 'scaled-1m-c16' });
          const args = module.workloadArguments(selected);
          if (!args.includes('--KeyLoadTests:TimeoutMinutes=140')
            || !args.includes('--KeyLoadTests:ScaleProfile=scaled-1m-c16')) process.exitCode = 1;
        } else {
          try { module.selectedScaleProfile({ KEYLOAD_SCALE_PROFILE: 'scaled-1m-c16', Benchmarks__EvidenceProfile: 'scaled-5m-c16' }); process.exitCode = 1; }
          catch (error) { if (error.message !== 'The native comparison profile identity is invalid.') process.exitCode = 1; }
          try { module.selectedScaleProfile({ KEYLOAD_SCALE_PROFILE: 'unknown', Benchmarks__EvidenceProfile: 'unknown' }); process.exitCode = 1; }
          catch (error) { if (error.message !== 'The native comparison profile identity is invalid.') process.exitCode = 1; }
        }
        """;
}
