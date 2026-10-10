using System.Diagnostics;
using System.Globalization;
using KeyLoad.CrashHost;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class SubscriptionFilterProcessTrial
{
    private const string DirectoryPrefix = "keyload-subscription-filter-crash-";
    private const string Dotnet = "dotnet";
    private const string GuidFormat = "N";
    private const string CrashMarker = "crash-point";
    private const int OriginalProcessSeconds = 20;

    internal static async Task RunAsync(CommitStage stage, int index, CancellationToken token)
    {
        var root = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
        using var admission = await StorageTrialLease.AcquireAsync(token);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(OriginalProcessSeconds), TimeProvider.System);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        using var process = Process.Start(Start(root, stage, index))!;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(await process.StandardOutput.ReadLineAsync(lifetime.Token)).IsEqualTo(CrashMarker);
            process.Kill();
            await process.WaitForExitAsync(lifetime.Token);
            await StorageRecoveryProcessTests.WaitForKilledProcessFilesAsync(root, lifetime.Token);
            await SubscriptionFilterProcessAssertions.RecoverAsync(root, stage, lifetime.Token);
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            if (!process.HasExited)
            { process.Kill(); await process.WaitForExitAsync(token); }
            if (Directory.Exists(root))
            { await StoragePublicationRecoveryTests.DeleteTrialAsync(root, token); }
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static ProcessStartInfo Start(string root, CommitStage stage, int index)
    {
        var start = new ProcessStartInfo(Dotnet) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { typeof(CrashHostMarker).Assembly.Location, root, stage.ToString(),
            index.ToString(CultureInfo.InvariantCulture), SubscriptionFilterCrashScenario.Mode })
        { start.ArgumentList.Add(argument); }
        return start;
    }
}
