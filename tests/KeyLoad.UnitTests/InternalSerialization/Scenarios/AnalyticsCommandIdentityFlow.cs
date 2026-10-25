using KeyLoad.UnitTests.Authorization;
using ManagedCode.Communication.Commands;
using A = KeyLoad.UnitTests.InternalSerialization.AnalyticsCommandIdentityAssertions;
using D = KeyLoad.UnitTests.InternalSerialization.AnalyticsCommandIdentityData;
using O = KeyLoad.UnitTests.AnalyticsStore.AnalyticsStoreTestOperations;

namespace KeyLoad.UnitTests.InternalSerialization;

internal static class AnalyticsCommandIdentityFlow
{
    internal static Task RunAsync(bool delete, CancellationToken token)
        => SafeProjectorOperationDatabase.RunAsync(async owner =>
        {
            var definition = new ResourceDefinition(D.Store, ResourceKind.AnalyticsStore, owner.Source.Partition.TransactionDomainId)
            {
                AnalyticsSchema = new([D.First, D.Last], [D.First, D.Last])
            };
            await O.EqualAsync(definition, owner.Source.Submit(OperationKind.ConfigureResource,
                new ConfigureResourceRequest(owner.Source.Partition.TenantId, owner.Source.Partition.DatabaseId, definition))
                .Get<ResourceDefinition>());
            owner.Source.Configure(D.Collection, ResourceKind.Collection);
            var expectedReference = A.Reference(owner);
            if (delete)
            {
                await SeedAsync(owner);
            }
            var id = Guid.NewGuid();
            var request = D.Request(owner.Source, id, delete, false);
            var first = Issue(owner, request);
            await A.OrderAsync(first, delete);
            var expected = new CommitReceipt(id, owner.NextToken(), [new("putDocument", D.Collection, D.Document, 1),
                new(delete ? AnalyticsStoreProtocol.DeleteKind : AnalyticsStoreProtocol.UpsertKind, D.Store, D.Last, delete ? 2 : 1)], DurabilityProfile.ProcessDurable, 1);
            await A.ReceiptAsync(expected, owner.Submit(first).Get<CommitReceipt>());
            await A.RowsAsync(owner, delete, expectedReference, token);
            await RetryAsync(owner, id, delete, first.Value!.Fingerprint, expected, expectedReference, token);
            await owner.ReopenAsync();
            await RetryAsync(owner, id, delete, first.Value.Fingerprint, expected, expectedReference, token);
        }, token);

    private static async Task SeedAsync(SafeProjectorOperationDatabase owner)
    {
        var id = Guid.NewGuid();
        var seed = new CommandRequest(id, owner.Source.Partition, [D.Analytics(false, false)]);
        var expected = new CommitReceipt(id, owner.NextToken(), [new(AnalyticsStoreProtocol.UpsertKind, D.Store, D.Last, 1)], DurabilityProfile.ProcessDurable, 1);
        await A.ReceiptAsync(expected, owner.Submit(Issue(owner, seed)).Get<CommitReceipt>());
    }

    private static async Task RetryAsync(SafeProjectorOperationDatabase owner, Guid id, bool delete,
        string originalIdentity, CommitReceipt expected, EntityRef expectedReference, CancellationToken token)
    {
        // Every retry is newly issued from the original public DTO.
        var retry = Issue(owner, D.Request(owner.Source, id, delete, true));
        await Assert.That(retry.Value!.Fingerprint).IsEqualTo(originalIdentity);
        await A.UnchangedAsync(owner, async () =>
        {
            await A.ReceiptAsync(expected, owner.Submit(retry).Get<CommitReceipt>());
            await A.RowsAsync(owner, delete, expectedReference, token);
            var changed = Issue(owner, D.Request(owner.Source, id, delete, true, changed: true));
            await Assert.That(changed.Value!.Fingerprint == originalIdentity).IsFalse();
            var refused = owner.Submit(changed);
            await Assert.That(Errors.Code(refused.Problem).Value).IsEqualTo(ErrorCode.Conflict);
            await Assert.That(refused.Problem?.Detail).IsEqualTo("The command ID was already used with different content.");

            await Assert.That(refused.Value).IsNull();
            await A.RowsAsync(owner, delete, expectedReference, token);
        });
    }

    private static Command<DatabaseCommand> Issue(SafeProjectorOperationDatabase owner, CommandRequest request)
    {
        var work = new ReadExecutionBudget(owner.Database.EvaluationClock, owner.CallerToken);
        return owner.Database.CreateCommand(OperationKind.Batch, request.CommandId, D.Root,
            owner.Database.EvaluationClock.GetUtcNow(), OrleansSerialization.Serialize(request), work);
    }
}
