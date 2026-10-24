using KeyLoad.UnitTests.ResourceExecution;

namespace KeyLoad.UnitTests.Messaging;

internal static class RemoteTransferCapacityAuthorityCold
{
    internal static async Task VerifyAsync(RemoteTransferDatabase fixture,
        IReadOnlyList<RemoteTransferCapacityAuthorityCompleted> completed)
    {
        await RemoteTransferCapacityAuthorityImages.AllAsync(fixture, completed);
        var original = RemoteTransferCapacityAuthorityStorage.Capture(fixture);
        var identity = fixture.Store.Identity with
        {
            SigningKey = fixture.Store.Identity.SigningKey.ToArray()
        };
        var position = fixture.Store.Position;
        fixture.Reopen();
        await RemoteTransferCapacityAuthorityStorage.IdentityAsync(fixture, identity, position);
        await RemoteTransferCapacityAuthorityStorage.ImageAsync(fixture, original, position);
        await RemoteTransferCapacityAuthorityImages.AllAsync(fixture, completed);
        foreach (var item in completed)
        {
            await ReplayAsync(fixture, item);
        }
        await RemoteTransferCapacityAuthorityStorage.ImageAsync(fixture, original, position);
    }

    internal static async Task ReplayAsync(RemoteTransferDatabase fixture, RemoteTransferCapacityAuthorityCompleted item)
    {
        await RemoteTransferCapacityAuthorityValues.ReplayAsync(fixture, item.Transfer.Create, item.Transfer.Created);
        await RemoteTransferCapacityAuthorityValues.ReplayAsync(fixture, item.Accept, item.Accepted);
        await RemoteTransferCapacityAuthorityValues.ReplayAsync(fixture, item.Complete, item.Completed);
        var image = RemoteTransferCapacityAuthorityStorage.Capture(fixture);
        var position = fixture.Store.Position;
        await ReplayResultAssertions.Same<CommitReceipt>(fixture.Apply(OperationKind.Delivery,
            item.Acknowledge, id: item.Acknowledge.CommandId), item.Acknowledged);
        var stale = fixture.Apply(OperationKind.Receive, item.Receive, id: item.Receive.RequestId);
        await Assert.That(Errors.Code(stale.Problem)).IsEqualTo(ErrorCode.StaleLease);
        await Assert.That(stale.Value).IsNull();

        var retained = OutcomeStoreOracle.ReadPartition(RemoteTransferCapacityAuthorityStorage.Execution(fixture), fixture.DestinationPartition,
            RemoteTransferDatabase.RootPrincipal, item.Receive.RequestId)
            ?? throw new InvalidOperationException("The original receive outcome is absent.");
        await ReplayResultAssertions.Same<ReceiveResult>(retained, item.Received);
        await RemoteTransferCapacityAuthorityStorage.ImageAsync(fixture, image, position);
    }
}
