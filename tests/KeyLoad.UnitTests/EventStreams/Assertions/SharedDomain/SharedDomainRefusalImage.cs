using KeyLoad.Core;
using KeyLoad.Storage;
using static KeyLoad.CrashHost.Features.EventStreams.SharedDomainProtocol;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class SharedDomainRefusalImage
{
    private const string Separator = ":";

    internal static string[] Capture(IAtomicStore store, PartitionRef partition, Guid id)
    {
        var ignored = new[] { KeySpace.PartitionOutcome(partition, Writer, id),
            KeySpace.OutcomeLocatorV2(partition, Writer, id), KeySpace.Clock.ToArray() }
            .Select(key => Convert.ToHexString(key) + Separator).ToArray();
        return SharedDomainNativeImage.Capture(store).Where(row => !ignored.Any(prefix => row.StartsWith(prefix, StringComparison.Ordinal))).ToArray();
    }

    internal static async Task RequireAsync(DatabaseEngine database, SharedDomainState state, PartitionRef partition,
        ReplicatedOperation operation, CancellationToken token)
    {
        var image = Capture(database.Store, partition, operation.Id);
        var position = database.Store.Position;
        var result = SharedDomainOperations.Apply(database, operation, token);
        await SharedDomainValueAssertions.FailureAsync(result, ErrorCode.Conflict, DomainDetail);
        await Assert.That(database.Store.Position).IsEqualTo(position + FirstRevision);
        await Assert.That(Capture(database.Store, partition, operation.Id)).IsEquivalentTo(image,
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await SharedDomainHistoryOperations.SaveAsync(database.Store, state, partition, operation, result);
    }
}
