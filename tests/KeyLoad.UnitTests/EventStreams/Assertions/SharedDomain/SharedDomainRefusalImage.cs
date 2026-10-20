using KeyLoad.Storage;
using ManagedCode.Communication.Commands;
using static KeyLoad.RecoveryTests.Features.EventStreams.SharedDomainProtocol;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class SharedDomainRefusalImage
{
    private const string Separator = ":";

    internal static string[] Capture(IAtomicStore store, PartitionRef partition, Guid id)
    {
        var ignored = new[] { KeySpace.PartitionOutcome(partition, Writer, id),
            KeySpace.OutcomeLocator(partition, Writer, id), KeySpace.Clock.ToArray(), KeySpace.AppliedBytes.ToArray() }
            .Select(key => Convert.ToHexString(key) + Separator).ToArray();
        return SharedDomainImage.Capture(store).Where(row => !ignored.Any(prefix => row.StartsWith(prefix, StringComparison.Ordinal))).ToArray();
    }

    internal static async Task RequireAsync(DatabaseEngine database, SharedDomainState state, PartitionRef partition,
        Command<DatabaseCommand> operation, CancellationToken token)
    {
        var image = Capture(database.Store, partition, operation.CommandId);
        var position = database.Store.Position;
        var canonicalPosition = database.LastApplied;
        var result = SharedDomainOperations.Apply(database, operation, token);
        await SharedDomainValueAssertions.FailureAsync(result, ErrorCode.Conflict, DomainDetail);
        await Assert.That(database.Store.Position).IsEqualTo(position + FirstRevision);
        await Assert.That(database.LastApplied).IsEqualTo(canonicalPosition + FirstRevision);
        await Assert.That(Capture(database.Store, partition, operation.CommandId)).IsEquivalentTo(image,
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await SharedDomainHistoryOperations.SaveAsync(database.Store, state, partition, operation, result);
    }
}
