using Aspire.Hosting.ApplicationModel;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Original complete CLI failure and six-resource refusal; published targets remain owned and unchanged.</summary>
internal static class ClusterRestoreRf3Rejections
{
    private const int FailedOperatorExit = 1;

    internal static async Task RequireAsync(ClusterRestoreRf3Fixture owner, ErrorCode expected, string? exactDetail,
        bool retainPublishedTarget, ClusterRestoreRf3OperatorObservation originalReader, CancellationToken cancellationToken)
    {
        await Assert.That(owner.OriginalOperatorExitCode).IsEqualTo(FailedOperatorExit);
        foreach (var node in ClusterRestoreRf3Protocol.Nodes)
        {
            var waiting = await owner.Application.ResourceNotifications.WaitForResourceAsync(node,
                value => value.Snapshot.State?.Text == KnownResourceStates.FailedToStart, cancellationToken).ConfigureAwait(false);
            await Assert.That(waiting.Snapshot.State?.Text).IsEqualTo(KnownResourceStates.FailedToStart);
        }
        if (retainPublishedTarget)
        { await Assert.That(Directory.Exists(owner.DataRoot)).IsTrue(); }
        else
        { await Assert.That(Directory.Exists(owner.DataRoot)).IsFalse(); }
        await Assert.That(File.Exists(owner.DataRoot)).IsFalse();
        await owner.StopAsync().ConfigureAwait(false);
        await originalReader.RequireFailureAsync(expected, exactDetail).ConfigureAwait(false);
    }
}
