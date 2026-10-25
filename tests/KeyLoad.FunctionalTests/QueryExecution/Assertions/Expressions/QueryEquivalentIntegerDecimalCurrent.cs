using System.Text.Json;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using static KeyLoad.FunctionalTests.QueryExecution.QueryEquivalentIntegerDecimalProtocol;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class QueryEquivalentIntegerDecimalCurrent
{
    internal static async Task PolicyAsync(TestDatabase fixture, long epoch, bool allowed, bool revoked)
    {
        var principal = new PrincipalRecord(Subject, Tenant,
            allowed ? [new(Database, Collection, Capability.Query | Capability.DocumentsRead)] : [], [])
        {
            PolicyEpoch = epoch,
            Revoked = revoked
        };
        await QueryEquivalentIntegerDecimalPages.EqualAsync(principal, fixture.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>());
        await QueryEquivalentIntegerDecimalPages.EqualAsync(principal,
            fixture.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(Subject))));
    }
    internal static async Task ReceiptAsync(TestDatabase fixture, CommandRequest seed, CommitReceipt receipt)
    {
        var expected = new CommitReceipt(seed.CommandId, new(fixture.Store.Identity.Incarnation, fixture.Partition.AtomicPartitionId, fixture.ReadStoredOutcome(seed.CommandId).CanonicalSequence), [.. Records.Select(row => new MutationReceipt(PutKind, Collection, row.Id, Revision))], DurabilityProfile.ProcessDurable, 1);
        await QueryEquivalentIntegerDecimalPages.EqualAsync(expected, receipt);
    }
    internal static async Task ReplayAsync(DatabaseEngine owner, ZoneTreeStore store,
        CommandRequest seed, CommitReceipt receipt, CancellationToken token)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(store);
        var cut = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        var failures = new List<Exception>();
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            var original = owner.CreateCommand(OperationKind.Batch, seed.CommandId, Root, default, JsonSerializer.Serialize(seed, JsonDefaults.Options));
            await QueryEquivalentIntegerDecimalPages.EqualAsync(receipt, owner.ApplyEmbedded(original,
                cancellationToken: token).Get<CommitReceipt>());
        }, failures);
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => CapabilityOutputOperations.ConserveAsync(store,
            image, cut, identity), failures);
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
