using System.Diagnostics;
using KeyLoad.CrashHost;
using KeyLoad.CrashHost.Features.ClusterReplication;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.StorageRecovery;
using P = KeyLoad.UnitTests.Features.ClusterReplication.SnapshotManifestEntryProtocol;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class SnapshotManifestEntryChild
{
    internal static async Task RunAsync(SnapshotManifestEntryOwners owner, string variant, CancellationToken token)
    {
        owner.ChildSettled = false;
        var start = new ProcessStartInfo(P.Dotnet)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[] { typeof(ReplicaCrashNode).Assembly.Location,
            SnapshotManifestEntryProbe.Mode, owner.Root, owner.Incarnation.ToString(P.GuidFormat), variant })
        {
            start.ArgumentList.Add(argument);
        }
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var child = await ExistingStoreInspectorSession.StartAsync(start, string.Empty);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await child.WaitAsync(cancelWhenReady: false, token);
                await child.SettleAsync(failures);
                await Assert.That(child.OriginalTasksJoined).IsTrue();
                await Assert.That(child.Process.HasExited).IsTrue();
                await Assert.That(child.Process.ExitCode).IsEqualTo(0);
                await Assert.That(child.Stdout.Text).IsEqualTo(SnapshotManifestEntryProbe.Refused + ":" + variant + Environment.NewLine);
                await Assert.That(child.Stderr.Text).IsEmpty();
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        owner.ChildSettled = true;
    }
}
