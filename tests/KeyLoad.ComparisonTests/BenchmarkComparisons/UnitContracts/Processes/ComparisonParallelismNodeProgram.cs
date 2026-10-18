namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class ComparisonParallelismNodeProgram
{
    internal const string Source = """
        import { pathToFileURL } from 'node:url';
        const { nativeSelection } = await import(pathToFileURL(process.argv[1]));
        const selection = ['--KeyLoadTests:Suite=comparison', '--KeyLoadTests:Filter=/*/*/ActualComparison/*',
          '--KeyLoadTests:ReportTrx=true', '--KeyLoadTests:TimeoutMinutes=140'];
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
        const retired = [];
        function rejectRetired(arguments_, environment, message) {
          try { nativeSelection(arguments_, environment); }
          catch (failure) {
            if (failure.message !== message) throw failure;
            retired.push(message);
            return;
          }
          throw new Error('Retired benchmark selection was accepted.');
        }
        for (const key of ['ScaleProfile', 'VectorProfile', 'OpenLoopRate', 'OpenLoopCancellationProof']) {
          for (const option of ['--KeyLoadTests:'+key, '--KeyLoadTests:'+key+'=', '--keyloadtests:'+key.toLowerCase()+'=old']) {
            rejectRetired([...selection, option], inherited, 'Retired benchmark selection is not supported.');
          }
          for (const [name, value] of [['KeyLoadTests__'+key, ''], ['Benchmarks__'+key, null],
            ['keyloadtests:'+key.toLowerCase(), undefined], ['BENCHMARKS__'+key.toLowerCase(), '']]) {
            rejectRetired(selection, { ...inherited, [name]: value }, 'Retired benchmark selection is not supported.');
          }
        }
        for (const clients of ['10', '500']) {
          rejectRetired([...selection, '--Benchmarks:Concurrency='+clients], inherited,
            'Benchmark workloads use their native session entry.');
        }
        if (JSON.stringify(selection) !== originalSelection || JSON.stringify(inherited) !== originalInherited) {
          throw new Error('Native selection changed the caller inputs.');
        }
        const healthy = nativeSelection(selection, inherited);
        process.stdout.write(JSON.stringify({ selected, explicit, rejected, retired, healthy }));
        """;
}
