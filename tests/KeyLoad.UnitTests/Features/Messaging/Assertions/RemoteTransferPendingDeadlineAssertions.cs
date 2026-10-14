using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class RemoteTransferPendingDeadlineAssertions
{
    private const string IncompleteImage = "The bounded remote-transfer fixture image has more canonical rows.";

    internal static async Task RequireAsync(RemoteTransferDatabase fixture, RemoteTransferDiscoveryClock clock)
    {
        var image = Image(fixture);
        var identity = NativeSerialization.Serialize(fixture.Store.Identity);
        var position = fixture.Store.Position;
        var native = (ZoneTreeStore)fixture.Store;
        var baseline = native.GetReadDiagnostics().RangeExaminedBytes;
        clock.Arm(() => native.GetReadDiagnostics().RangeExaminedBytes > baseline,
            fixture.Database.DueDiscoveryDeadline);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => RemoteTransferPendingDiscovery.Read(
            fixture.Database, RemoteTransferDatabase.RootPrincipal, null, CancellationToken.None));
        await Assert.That(clock.ExpiredAfterNativeProgress).IsTrue();
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(failure.Message).IsEqualTo(DueWorkProtocol.DeadlineExceeded);
        clock.Disarm();
        await Assert.That(Image(fixture).SequenceEqual(image)).IsTrue();
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
        await Assert.That(NativeSerialization.Serialize(fixture.Store.Identity).SequenceEqual(identity)).IsTrue();
    }

    private static string[] Image(RemoteTransferDatabase fixture) => fixture.Store.Read(view =>
    {
        var page = view.Scan([], fixture.Database.Limits.MaxResults);
        if (page.HasMore)
        {
            throw new InvalidOperationException(IncompleteImage);
        }
        return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" +
            Convert.ToHexString(row.Value.Span)).ToArray();
    });
}
