namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class ComparisonParallelismNodeProgram
{
    internal const string Source = """
        import { pathToFileURL } from 'node:url';
        const { nativeSelection } = await import(pathToFileURL(process.argv[1]));
        const concurrency = process.argv[2];
        const selection = ['--KeyLoadTests:Suite=comparison', '--KeyLoadTests:Filter=/*/*/ActualComparison/*',
          '--KeyLoadTests:ReportTrx=true', '--KeyLoadTests:TimeoutMinutes=140', '--Benchmarks:Concurrency='+concurrency];
        const inherited = { GITHUB_SHA: 'original-revision', Benchmarks__Target: 'KeyLoad' };
        const originalSelection = JSON.stringify(selection);
        const originalInherited = JSON.stringify(inherited);
        const selected = nativeSelection(selection, inherited);
        const explicit = nativeSelection([...selection, '--KeyLoadTests:Execution:MaximumParallelTests=1'], inherited);
        const rejected = [];
        for (const value of ['2', '20', '50', '51', '0', '-1', '1.5', 'invalid']) {
          try { nativeSelection([...selection, '--KeyLoadTests:Execution:MaximumParallelTests='+value], inherited); }
          catch (failure) { rejected.push({ value, message: failure.message }); continue; }
          throw new Error('Concurrent or invalid comparison selection was accepted.');
        }
        if (JSON.stringify(selection) !== originalSelection || JSON.stringify(inherited) !== originalInherited) {
          throw new Error('Native selection changed the caller inputs.');
        }
        const healthy = nativeSelection(selection, inherited);
        process.stdout.write(JSON.stringify({ selected, explicit, rejected, healthy }));
        """;
}
