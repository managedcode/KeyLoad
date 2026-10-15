using KeyLoad.Core;
using KeyLoad.CrashHost.Features.EventStreams;
using KeyLoad.Storage;
using static KeyLoad.CrashHost.Features.EventStreams.SharedDomainProtocol;

namespace KeyLoad.RecoveryTests.Features.EventStreams;

internal static class SharedDomainRecoverySeed
{
    private const int SeedProducers = 4;
    private const int ReceiveIndex = 4;
    private const int SeedOperations = 6;
    private const int OriginalLeaseSeconds = 30;

    internal static async Task<List<SharedDomainRecoveryHistory>> VerifyAsync(DatabaseEngine database, IAtomicStore store, SharedDomainRecoveryInput input)
    {
        await Assert.That(input.SeedOperations.Length).IsEqualTo(SeedOperations);
        await Assert.That(input.SeedResults.Length).IsEqualTo(SeedOperations);
        var scopes = SharedDomainCorpus.Create(Tenant, Database);
        List<SharedDomainRecoveryHistory> history = [];
        for (var index = FirstIndex; index < SeedOperations; index++)
        {
            var operation = input.SeedOperations[index];
            await Assert.That(operation.EvaluatedAt).IsEqualTo(input.Original.EvaluatedAt);
            await Assert.That(operation.PrincipalId).IsEqualTo(Writer);
            var position = input.SeedPosition - (SeedOperations - index - FirstRevision);
            var scope = index < SeedProducers ? scopes[index + SecondIndex] : scopes[SecondIndex];
            if (index < SeedProducers)
            {
                await Assert.That(operation.Kind).IsEqualTo(OperationKind.Batch);
                await Assert.That(operation.Id).IsEqualTo(CommandId);
                await SharedDomainRecoveryValues.ProducerAsync(input.SeedResults[index], scope, CommandId, Item, store.Identity.Incarnation, position);
            }
            else if (index == ReceiveIndex)
            {
                await ReceiveAsync(database, operation, input.SeedResults[index], scope, position);
            }
            else
            {
                await AckAsync(operation, input.SeedResults[index], scope, position, store.Identity.Incarnation);
            }
            var actual = OutcomeStoreOracle.Read(store, operation) ?? throw new InvalidOperationException(MissingNative);
            await SharedDomainRecoveryValues.ResultAsync(actual, input.SeedResults[index]);
            var entry = Capture(store, operation, actual, scope.Partition);
            await Assert.That(entry.OutcomeBytes.AsSpan().SequenceEqual(OriginalBytes(input.SeedImage, entry.OutcomeKey))).IsTrue();
            await Assert.That(entry.LocatorBytes.AsSpan().SequenceEqual(OriginalBytes(input.SeedImage, entry.LocatorKey))).IsTrue();
            history.Add(entry);
        }
        return history;
    }

    private static async Task ReceiveAsync(DatabaseEngine database, ReplicatedOperation operation, OperationResult result,
        SharedDomainScope scope, long position)
    {
        await Assert.That(operation.Kind).IsEqualTo(OperationKind.Receive);
        var received = result.Get<ReceiveResult>();
        await Assert.That(received.RequestId).IsEqualTo(operation.Id);
        await Assert.That(received.Token).IsEqualTo(new CommitToken(database.Store.Identity.Incarnation,
            scope.Partition.AtomicPartitionId, position, Generation));
        var actual = await Assert.That(received.Deliveries).HasSingleItem();
        await SharedDomainRecoveryValues.EqualAsync(actual, new Delivery(Item, scope.Payload, Headers, actual.Token,
            FirstRevision, operation.EvaluatedAt.AddSeconds(OriginalLeaseSeconds), checked((int)FirstRevision), Generation));
        await SharedDomainRecoveryValues.EqualAsync(database.Verify<DeliveryClaims>(actual.Token),
            new DeliveryClaims(new(scope.Partition, scope.Queue!), Item, Writer, FirstRevision, Generation, database.Store.Identity.Incarnation));
    }

    private static async Task AckAsync(ReplicatedOperation operation, OperationResult result, SharedDomainScope scope, long position, Guid incarnation)
    {
        await Assert.That(operation.Kind).IsEqualTo(OperationKind.Delivery);
        await Assert.That(result.Error).IsNull();
        await Assert.That(result.SafeDetail).IsNull();
        await Assert.That(result.Json).IsNull();
        await SharedDomainRecoveryValues.ReceiptValueAsync(result.Get<CommitReceipt>(), new CommitReceipt(operation.Id,
            new(incarnation, scope.Partition.AtomicPartitionId, position, Generation),
            [new(nameof(DeliveryAction.Ack), scope.Queue!, Item, AckVersion)], DurabilityProfile.ProcessDurable));
    }

    internal static SharedDomainRecoveryHistory Capture(IAtomicStore store, ReplicatedOperation operation, OperationResult result, PartitionRef partition)
    {
        var key = KeySpace.PartitionOutcome(partition, Writer, operation.Id);
        var locator = KeySpace.OutcomeLocatorV2(partition, Writer, operation.Id);
        return new(operation, result, partition, key, Required(store, key), locator, Required(store, locator));
    }

    private static byte[] OriginalBytes(string[] rows, byte[] key)
    {
        var prefix = Convert.ToHexString(key) + RowSeparator;
        return Convert.FromHexString(rows.Single(row => row.StartsWith(prefix, StringComparison.Ordinal))[prefix.Length..]);
    }
    private const string RowSeparator = ":";

    internal static byte[] Required(IAtomicStore store, byte[] key)
        => store.Read(view => view.ReadOwnedValue(key)) ?? throw new InvalidOperationException(MissingNative);
}
