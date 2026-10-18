using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.UnitTests.Features.Authorization;
using D = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreNativeTestData;
using O = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreNativeTestOperations;
using P = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsCompositionData;

namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal static class AnalyticsCompositionOperations
{
    internal static Command<DatabaseCommand> Issue<T>(SafeProjectorOperationDatabase owner, OperationKind kind,
        T payload, Guid id, string principal = D.Root)
    {
        var work = new ReadExecutionBudget(owner.Database.OperationLimitsOptions, owner.Database.EvaluationClock, owner.CallerToken);
        return owner.Database.CreateCommand(kind, id, principal,
            owner.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(payload), work);
    }

    internal static async Task ConfigureAsync(SafeProjectorOperationDatabase owner)
    {
        ResourceDefinition[] definitions = [new(D.Store, ResourceKind.AnalyticsStore, owner.Source.Partition.TransactionDomainId)
            { AnalyticsSchema = new([D.Kind], [D.Value]) },
            new(D.AnchorCollection, ResourceKind.Collection, owner.Source.Partition.TransactionDomainId),
            new(P.Graph, ResourceKind.Graph, owner.Source.Partition.TransactionDomainId),
            new(P.Events, ResourceKind.StreamSet, owner.Source.Partition.TransactionDomainId)];
        foreach (var definition in definitions)
        {
            var operation = Issue(owner, OperationKind.ConfigureResource,
                new ConfigureResourceRequest(owner.Source.Partition.TenantId, owner.Source.Partition.DatabaseId, definition),
                definition.Name == D.AnchorCollection ? AnalyticsExpectedResourceCreation.Anchor : Guid.NewGuid());
            await O.EqualAsync(definition, owner.Submit(operation).Get<ResourceDefinition>());
        }
        await PolicyAsync(owner, 1, true);
    }

    internal static async Task PolicyAsync(SafeProjectorOperationDatabase owner, long epoch, bool write)
    {
        var principal = P.Principal(owner, epoch, write);
        var operation = Issue(owner, OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal), Guid.NewGuid());
        await O.EqualAsync(principal, owner.Submit(operation).Get<PrincipalRecord>());
    }

    internal static Command<DatabaseCommand> Batch(SafeProjectorOperationDatabase owner, ImmutableArray<Mutation> mutations,
        string principal = P.Writer)
    {
        var id = Guid.NewGuid();
        return Issue(owner, OperationKind.Batch, new CommandRequest(id, owner.Source.Partition, mutations), id, principal);
    }

    internal static async Task<AnalyticsCompositionCommit> CommitAsync(SafeProjectorOperationDatabase owner,
        Command<DatabaseCommand> operation, ImmutableArray<MutationReceipt> receipts, long creatorEpoch = 1)
    {
        var expected = new CommitReceipt(operation.CommandId, owner.NextToken(), receipts, DurabilityProfile.ProcessDurable, 1);
        var actual = owner.Submit(operation).Get<CommitReceipt>();
        await ReceiptAsync(expected, actual);
        return new(operation, expected, creatorEpoch);
    }

    internal static async Task ReceiptAsync(CommitReceipt expected, CommitReceipt actual)
    {
        await O.EqualAsync(expected, actual);
        foreach (var mutation in actual.Mutations)
        {
            await Assert.That(mutation.CompositionReferences.IsEmpty).IsTrue();
        }
    }

    internal static async Task ReplayAsync(SafeProjectorOperationDatabase owner, AnalyticsCompositionCommit original, long currentEpoch = 3)
    {
        var image = TeamRowPolicyAssertions.Image(owner.Store);
        var position = owner.Store.Position;
        await ReceiptAsync(original.Receipt, owner.Database.Apply(original.Operation).Get<CommitReceipt>());
        var fresh = owner.Submit(original.Operation);
        if (original.Operation.UserId! == P.Writer && original.CreatorEpoch != currentEpoch)
        {
            await O.EqualAsync(Result<object>.Fail(Errors.Problem(ErrorCode.PermissionDenied, "The principal policy changed since this command was evaluated.")), fresh);
            await Assert.That(fresh.Value).IsNull();
        }
        else
        {
            await ReceiptAsync(original.Receipt, fresh.Get<CommitReceipt>());
        }

        await Assert.That(owner.Store.Position).IsEqualTo(position);
        await Assert.That(TeamRowPolicyAssertions.Image(owner.Store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
    }
}
