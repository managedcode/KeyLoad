using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Authorization;
using static KeyLoad.UnitTests.Features.QueryExecution.BinderFieldLineageProtocol;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class BinderFieldLineageAuthority
{
    internal static async Task ConfigureAsync(TestDatabase fixture, string subject, int mode, long epoch, bool revoked)
    {
        var principal = new PrincipalRecord(subject, Tenant,
            [new(Database, Collection, Capability.DocumentsRead | Capability.Query)], mode switch
            {
                ReadOnly => [RawRead],
                UseOnly => [RawUse],
                Both => [RawRead, RawUse],
                _ => []
            })
        {
            PolicyEpoch = epoch,
            Revoked = revoked
        };
        await BinderFieldLineagePages.EqualAsync(principal, fixture.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>());
        await BinderFieldLineagePages.EqualAsync(principal,
            fixture.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(subject))));
    }

    internal static async Task SeedReceiptAsync(TestDatabase fixture, CommandRequest command, CommitReceipt receipt)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(fixture.Store.Identity.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(fixture.Partition.AtomicPartitionId);

        await Assert.That(receipt.Token.Position).IsEqualTo(fixture.Store.Position);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await BinderFieldLineagePages.EqualAsync<MutationReceipt[]>([new(PutDocumentKind, Collection, DocumentId, OriginalRevision)],
            receipt.Mutations.ToArray());
    }

    internal static async Task ReplayAsync(DatabaseEngine database, ZoneTreeStore store,
        CommandRequest command, CommitReceipt receipt, CancellationToken token)
    {
        var image = TeamRowPolicyAssertions.Image(store);
        var cut = store.Position;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var original = database.CreateCommand(OperationKind.Batch, command.CommandId, Root, default, JsonSerializer.Serialize(command, JsonDefaults.Options));
            await BinderFieldLineagePages.EqualAsync(receipt,
                database.ApplyEmbedded(original, cancellationToken: token).Get<CommitReceipt>());
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => TeamRowPolicyAssertions.UnchangedAsync(store, image, cut), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
