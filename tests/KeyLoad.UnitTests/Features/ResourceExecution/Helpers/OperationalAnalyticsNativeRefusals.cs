using KeyLoad.Server;
using Microsoft.Extensions.Options;
using P = KeyLoad.UnitTests.Features.ResourceExecution.OperationalAnalyticsNativeProtocol;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class OperationalAnalyticsNativeRefusals
{
    internal static async Task SecondOwnerAsync(OperationalAnalyticsNativeOwner owner, CancellationToken token)
    {
        var opened = false;
        _ = await Assert.ThrowsExactlyAsync<IOException>(async () =>
        {
            await using var duplicate = new OperationalAnalyticsOwner(owner.NodeDirectory,
                Options.Create(new OperationalAnalyticsOptions()), TimeProvider.System);
            opened = true;
            await duplicate.FlushAsync(token);
        });
        await Assert.That(opened).IsFalse();
    }

    internal static async Task AdmissionAsync(OperationalAnalyticsOwner analytics, OperationalAnalyticsNativeCut cut,
        DateTimeOffset from, DateTimeOffset to, CancellationToken token)
    {
        var range = await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
            analytics.ReadAsync(from.AddTicks(P.MisalignedTicks), to, token));
        ArgumentNullException.ThrowIfNull(range);
        await Assert.That(range.ParamName).IsEqualTo(P.RangeParameter);
        var span = await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
            analytics.ReadAsync(from.AddDays(-P.RejectedDays), to, token));
        ArgumentNullException.ThrowIfNull(span);
        await Assert.That(span.ParamName).IsEqualTo(P.RangeParameter);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        var failure = await Assert.ThrowsAsync<OperationCanceledException>(() => analytics.ReadAsync(from, to, canceled.Token));
        ArgumentNullException.ThrowIfNull(failure);
        await Assert.That(failure.CancellationToken).IsEqualTo(canceled.Token);
        var invalid = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            analytics.TryRecord(cut.Snapshot with
            {
                CommittedIndex = P.InvalidPosition
            }));
        await Assert.That(invalid.ParamName).IsEqualTo(P.SnapshotParameter);
        var inexact = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            analytics.TryRecord(cut.Snapshot with
            {
                LastIndex = P.InexactPosition,
                CommittedIndex = P.InexactPosition
            }));
        await Assert.That(inexact.ParamName).IsEqualTo(P.SnapshotParameter);
    }
}
