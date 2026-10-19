namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class ComparisonParallelismNodeProgram
{
    internal const string Source = """
        import { pathToFileURL } from 'node:url';
        const { runtimeSelection } = await import(pathToFileURL(process.argv[1]));
        const selection = ['--KeyLoadTests:Suite=comparison', '--KeyLoadTests:Filter=/*/*/ActualComparison/*',
          '--KeyLoadTests:ReportTrx=true', '--KeyLoadTests:TimeoutMinutes=140'];
        const inherited = { GITHUB_SHA: 'original-revision', Benchmarks__Target: 'KeyLoad' };
        const originalSelection = JSON.stringify(selection);
        const originalInherited = JSON.stringify(inherited);
        const selected = runtimeSelection(selection, inherited);
        const explicit = runtimeSelection([...selection, '--KeyLoadTests:Execution:MaximumParallelTests=1'], inherited);
        const rejected = [];
        for (const value of ['2', '20', '50', '51', '0', '-1', '1.5', 'invalid']) {
          try { runtimeSelection([...selection, '--KeyLoadTests:Execution:MaximumParallelTests='+value], inherited); }
          catch (failure) { rejected.push({ value, message: failure.message }); continue; }
          throw new Error('Concurrent or invalid comparison selection was accepted.');
        }
        const invalid = [];
        function rejectInvalid(argument, message) {
          try { runtimeSelection([...selection, argument], inherited); }
          catch (failure) {
            if (failure.message !== message) throw failure;
            invalid.push(message);
            return;
          }
          throw new Error('Invalid execution test selection was accepted.');
        }
        rejectInvalid('--KeyLoadTests:Suite=comparison', 'Invalid or duplicate execution test selection.');
        rejectInvalid('--KeyLoadTests:Unknown=true', 'Unsupported execution test argument.');
        rejectInvalid('--KeyLoadTests:Filter=', 'Invalid or duplicate execution test selection.');
        if (JSON.stringify(selection) !== originalSelection || JSON.stringify(inherited) !== originalInherited) {
          throw new Error('Execution selection changed the caller inputs.');
        }
        const healthy = runtimeSelection(selection, inherited);
        process.stdout.write(JSON.stringify({ selected, explicit, rejected, invalid, healthy }));
        """;
}
