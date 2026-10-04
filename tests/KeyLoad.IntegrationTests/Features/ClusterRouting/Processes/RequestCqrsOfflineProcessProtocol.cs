using System.Diagnostics;
using System.Globalization;
using KeyLoad.IntegrationTests.Features.StorageRecovery;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsOfflineProcessProtocol
{
    internal const string Executable = "node";
    internal const string InputType = "--input-type=module";
    internal const string Eval = "--eval";
    internal const string SuccessError = "bounded-child-λ\n";
    internal const string ReleaseSignal = "release";
    internal const string InvalidMarker = "The owned child marker was invalid.";
    internal const string MarkerTimeout = "The owned child did not publish its live-process marker.";
    internal const string MissingFailure = "The owned child completed without the expected failure.";
    internal static readonly TimeSpan MarkerDeadline = TimeSpan.FromSeconds(10);
    internal const int MarkerBytes = 128;

    internal static ProcessStartInfo StartSuccess()
        => Start("process.stdout.write('A'.repeat(Number(process.argv[1]))); process.stderr.write('bounded-child-λ\\n');",
            NodeEpochRf3OfflineProtocol.MaximumOutputBytes.ToString(CultureInfo.InvariantCulture));

    internal static ProcessStartInfo StartWaitingChild(string marker, string nonce, bool oversized,
        string? release = null)
    {
        var script = "import { writeFileSync, existsSync } from 'node:fs'; "
            + "writeFileSync(process.argv[1], process.pid + ':' + process.argv[2]); "
            + (oversized
                ? "await new Promise(resolve => { const poll = setInterval(() => { "
                    + "if (existsSync(process.argv[4])) { clearInterval(poll); resolve(); } }, 10); }); "
                    + "process.stdout.write('A'.repeat(Number(process.argv[3]))); "
                : string.Empty)
            + "setInterval(() => {}, 1000); await new Promise(() => {});";
        var arguments = oversized
            ? new[] { marker, nonce, (NodeEpochRf3OfflineProtocol.MaximumOutputBytes + 1)
                .ToString(CultureInfo.InvariantCulture), release
                ?? throw new ArgumentNullException(nameof(release)) }
            : new[] { marker, nonce };
        return Start(script, arguments);
    }

    private static ProcessStartInfo Start(string script, params string[] arguments)
    {
        var start = new ProcessStartInfo(Executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false
        };
        start.ArgumentList.Add(InputType);
        start.ArgumentList.Add(Eval);
        start.ArgumentList.Add(script);
        foreach (var argument in arguments)
        { start.ArgumentList.Add(argument); }
        return start;
    }
}
