using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class NativeTestSelectionProcess
{
    internal static async Task<string> ReadAsync(string suite)
    {
        const string program = """
            import { pathToFileURL } from 'node:url';
            const { nativeSelection } = await import(pathToFileURL(process.argv[1]));
            const suite = process.argv[2];
            for (const bad of [[], ['--KeyLoadTests:Suite=unknown'], ['--KeyLoadTests:Suite=unit','--KeyLoadTests:Suite=rf3'],
              ['--KeyLoadTests:Suite=unit','--KeyLoadTests:Execution:MaximumParallelTests=65'],
              ['--KeyLoadTests:Suite=unit','--KeyLoadTests:NativeCoverage:ServerMode=wrong']]) {
              let rejected = false; try { nativeSelection(bad, {}); } catch { rejected = true; }
              if (!rejected) throw new Error('Invalid native test selection was accepted.');
            }
            const selected = nativeSelection(['--KeyLoadTests:Suite='+suite, '--KeyLoadTests:Filter=/*/*/ActualCase/*',
              '--KeyLoadTests:ReportTrx=true', '--KeyLoadTests:CoverageSettings=settings.xml',
              '--KeyLoadTests:CoverageOutput=coverage.xml'], { GITHUB_SHA: 'original-revision' });
            console.log(JSON.stringify(selected));
            """;
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "KeyLoad.slnx")))
        { directory = directory.Parent; }
        var root = directory?.FullName ?? throw new DirectoryNotFoundException("Native selection regression requires the checkout.");
        var start = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { "--input-type=module", "--eval", program,
            Path.Combine(root, "scripts", "Features", "TestInfrastructure", "run-tests.mjs"), suite })
        { start.ArgumentList.Add(argument); }
        using var process = new Process { StartInfo = start };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15), TimeProvider.System);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, TestContext.Current!.Execution.CancellationToken);
        if (!process.Start())
        { throw new InvalidOperationException("Native selection Node process did not start."); }
        var output = process.StandardOutput.ReadToEndAsync(lifetime.Token);
        var error = process.StandardError.ReadToEndAsync(lifetime.Token);
        try
        {
            await process.WaitForExitAsync(lifetime.Token).ConfigureAwait(false);
            var results = await Task.WhenAll(output, error).ConfigureAwait(false);
            await Assert.That(process.ExitCode).IsEqualTo(0);
            await Assert.That(results[1]).IsEmpty();
            return results[0];
        }
        finally
        {
            if (!process.HasExited)
            { process.Kill(entireProcessTree: true); }
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3), TimeProvider.System);
            await process.WaitForExitAsync(cleanup.Token).ConfigureAwait(false);
            await ((Task)Task.WhenAll(output, error)).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}
