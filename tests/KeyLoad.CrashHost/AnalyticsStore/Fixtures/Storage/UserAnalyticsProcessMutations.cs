using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Server;
using P = KeyLoad.CrashHost.Features.AnalyticsStore.UserAnalyticsProcessProtocol;

namespace KeyLoad.CrashHost.Features.AnalyticsStore;

internal static class UserAnalyticsProcessMutations
{
    internal static void Bootstrap(string root, PartitionHost host, CancellationToken token)
    {
        var bootstrap = new BootstrapPhysicalShardCatalogRequest(P.CatalogVersion, P.Absent, UserAnalyticsProcessOptions.Create(root).Value.PhysicalShardId,
            host.Configuration.Incarnation, host.Configuration.VoterIds);
        var original = Issue(host, OperationKind.BootstrapPhysicalShardCatalog, bootstrap,
            PhysicalShardCatalogIdentity.CreateBootstrapCommandId(UserAnalyticsProcessOptions.Create(root).Value.PhysicalShardId), token);
        UserAnalyticsProcessState.Equal(true, host.Database.ApplyEmbedded(original, token).Get<bool>());
        var definition = new ResourceDefinition(P.Store, ResourceKind.AnalyticsStore, P.Domain)
        {
            AnalyticsSchema = new([P.Region], [P.Amount])
        };
        var configure = Issue(host, OperationKind.ConfigureResource,
            new ConfigureResourceRequest(P.Tenant, P.Database, definition), Guid.NewGuid(), token);
        UserAnalyticsProcessState.Equal(definition, host.Database.ApplyEmbedded(configure, token).Get<ResourceDefinition>());
    }

    internal static (ReplicatedOperation Operation, CommitReceipt Receipt) Write(PartitionHost host, bool update,
        CancellationToken token)
    {
        var mutation = new UpsertAnalyticsEvents(P.Store,
            update ? [P.Event(P.First, P.UpdatedValue)] : [P.Event(P.First, P.FirstValue), P.Event(P.Second, P.SecondValue)],
            update ? ImmutableDictionary<string, long>.Empty.Add(P.First, P.Created)
                : ImmutableDictionary<string, long>.Empty.Add(P.First, P.Absent).Add(P.Second, P.Absent));
        var command = new CommandRequest(Guid.NewGuid(), P.Partition, [mutation]);
        var original = Issue(host, OperationKind.Batch, command, command.CommandId, token);
        // This is the existing physical-host receipt label, not evidence of a running quorum.
        var expected = new CommitReceipt(command.CommandId, new(host.Database.Store.Identity.Incarnation,
            P.Partition.AtomicPartitionId, checked(host.Database.Store.Position + P.Created), P.Created),
            [new(AnalyticsStoreProtocol.UpsertKind, P.Store, update ? P.First : P.Second, update ? P.Updated : P.Created)],
            DurabilityProfile.QuorumProcessDurable);
        token.ThrowIfCancellationRequested();
        var actual = host.Database.Apply(original).Get<CommitReceipt>();
        UserAnalyticsProcessState.Equal(expected, actual);
        RequirePrivateEmpty(actual);
        return (original, expected);
    }

    internal static void RequirePrivateEmpty(CommitReceipt receipt)
    {
        if (receipt.Mutations.Any(row => !row.CompositionReferences.IsEmpty))
        {
            throw new InvalidOperationException(P.Invalid);
        }
    }

    private static ReplicatedOperation Issue(PartitionHost host, OperationKind kind, object payload, Guid id, CancellationToken token)
    {
        var work = new ReadExecutionBudget(host.Database.OperationLimitsOptions, host.Database.EvaluationClock, token);
        return host.Database.CreateNativeOperation(kind, id, PartitionStoreProtocol.AdministratorId,
            TimeProvider.System.GetUtcNow(), NativeSerialization.Serialize(payload), work);
    }
}
