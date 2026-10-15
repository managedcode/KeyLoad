using KeyLoad.Server;
using P = KeyLoad.UnitTests.Features.ResourceExecution.OperationalAnalyticsNativeProtocol;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class OperationalAnalyticsNativeFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var owner = new OperationalAnalyticsNativeOwner();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var from = Hour(TimeProvider.System.GetUtcNow());
                var original = owner.Open();
                var cut = await OperationalAnalyticsNativeSeed.RecordAsync(owner, original, token);
                await original.FlushAsync(token);
                var to = Hour(TimeProvider.System.GetUtcNow()).AddHours(P.NextHour);
                await OperationalAnalyticsNativeAssertions.ReportAsync(original, from, to, originalOwner: true, token);
                await OperationalAnalyticsNativeRefusals.SecondOwnerAsync(owner, token);
                await OperationalAnalyticsNativeRefusals.AdmissionAsync(original, cut, from, to, token);
                await OperationalAnalyticsNativeAssertions.ReportAsync(original, from, to, originalOwner: true, token);
                await OperationalAnalyticsNativeAssertions.CanonicalAsync(owner, cut);
                await OperationalAnalyticsNativeAssertions.PrivateStorageAsync(owner);
                await owner.CloseAsync();
                await Assert.That(original.HasJoinedEngine).IsTrue();
                Assert.ThrowsExactly<ObjectDisposedException>(() => original.TryRecord(cut.Snapshot));
                for (var cycle = 0; cycle < P.ColdCycles; cycle++)
                {
                    var cold = owner.Open();
                    await OperationalAnalyticsNativeAssertions.ReportAsync(cold, from, to, originalOwner: false, token);
                    await OperationalAnalyticsNativeAssertions.CanonicalAsync(owner, cut);
                    await OperationalAnalyticsNativeAssertions.PrivateStorageAsync(owner);
                    if (cycle == P.ColdCycles - P.LastCycleOffset)
                    {
                        await OperationalAnalyticsNativeCancellation.RunAsync(owner, cold, from, to, token);
                    }
                    else
                    {
                        await owner.CloseAsync();
                        await Assert.That(cold.HasJoinedEngine).IsTrue();
                    }
                }
            }, failures);
            if (failures.Count != 0)
            {
                owner.RetainEvidence();
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static DateTimeOffset Hour(DateTimeOffset value)
        => new(value.UtcTicks - value.UtcTicks % TimeSpan.TicksPerHour, TimeSpan.Zero);
}
