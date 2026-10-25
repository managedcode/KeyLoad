using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Messaging;

internal static class RemoteTransferPendingDeadlineAssertions
{
    private const string IncompleteImage = "The bounded remote-transfer fixture image has more canonical rows.";

    internal static async Task RequireAsync(RemoteTransferDatabase fixture, RemoteTransferDiscoveryClock clock)
    {
        var image = Image(fixture);
        var identity = OrleansSerialization.Serialize(fixture.Store.Identity);
        var position = fixture.Store.Position;
        var execution = (ZoneTreeStore)fixture.Store;
        var baseline = execution.GetReadDiagnostics().RangeExaminedBytes;
        clock.Arm(() => execution.GetReadDiagnostics().RangeExaminedBytes > baseline,
            fixture.Database.DueDiscoveryDeadline);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => RemoteTransferPendingDiscovery.Read(
            fixture.Database, RemoteTransferDatabase.RootPrincipal, null, CancellationToken.None));
        await Assert.That(clock.ExpiredAfterProgress).IsTrue();
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(failure.Message).IsEqualTo(DueWorkProtocol.DeadlineExceeded);
        clock.Disarm();
        await Assert.That(Image(fixture).SequenceEqual(image)).IsTrue();
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
        await Assert.That(OrleansSerialization.Serialize(fixture.Store.Identity).SequenceEqual(identity)).IsTrue();
    }

    private static string[] Image(RemoteTransferDatabase fixture) => fixture.Store.Read(view =>
    {
        var page = view.Scan([], view.MaximumRangeRecords);
        if (page.HasMore)
        {
            throw new InvalidOperationException(IncompleteImage);
        }
        return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" +
            Convert.ToHexString(row.Value.Span)).ToArray();
    });
}
