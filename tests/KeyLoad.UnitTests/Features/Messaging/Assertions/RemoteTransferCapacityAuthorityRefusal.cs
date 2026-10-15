using static KeyLoad.UnitTests.Features.Messaging.RemoteTransferCapacityAuthorityProtocol;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class RemoteTransferCapacityAuthorityRefusal
{
    internal static async Task RequireAsync(RemoteTransferDatabase fixture, bool source,
        RemoteTransferCapacityAuthorityFault fault, RemoteTransferCapacityAuthorityCompleted original,
        CommandRequest applicationReplay, CommandRequest fresh, string[] image, long position)
    {
        foreach (var request in new[] { applicationReplay, fresh })
        {
            var error = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Apply(OperationKind.Batch, request));
            await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(error.Message).IsEqualTo(Corrupt);
            await RemoteTransferCapacityAuthorityStorage.ImageAsync(fixture, image, position);
            await Assert.That(OutcomeStoreOracle.ReadPartition(RemoteTransferCapacityAuthorityStorage.Native(fixture), request.Partition,
                RemoteTransferDatabase.RootPrincipal, request.CommandId)).IsNull();
        }
        if (fault == RemoteTransferCapacityAuthorityFault.MissingRecords)
        {
            await Assert.That(source ? fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal,
                fixture.SourceQueue, original.Transfer.Mutation.TransferId) is null
                : fixture.Database.InspectQueueTransferReceipt(RemoteTransferDatabase.RootPrincipal,
                    fixture.DestinationQueue, fixture.SourceQueue, original.Transfer.Mutation.TransferId) is null).IsTrue();
        }
        else
        {
            var error = Assert.ThrowsExactly<KeyLoadException>(() => Inspect(fixture, source, original));
            await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(error.Message).IsEqualTo(Corrupt);
        }
        await RemoteTransferCapacityAuthorityStorage.ImageAsync(fixture, image, position);
    }

    private static void Inspect(RemoteTransferDatabase fixture, bool source, RemoteTransferCapacityAuthorityCompleted original)
    {
        if (source)
        {
            _ = fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal, fixture.SourceQueue, original.Transfer.Mutation.TransferId);
        }
        else
        {
            _ = fixture.Database.InspectQueueTransferReceipt(RemoteTransferDatabase.RootPrincipal,
                fixture.DestinationQueue, fixture.SourceQueue, original.Transfer.Mutation.TransferId);
        }
    }
}
