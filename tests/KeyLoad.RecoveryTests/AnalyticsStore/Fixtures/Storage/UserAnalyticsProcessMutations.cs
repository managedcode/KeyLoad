using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Server;
using ManagedCode.Communication.Commands;
using P = KeyLoad.RecoveryTests.AnalyticsStore.UserAnalyticsProcessProtocol;

namespace KeyLoad.RecoveryTests.AnalyticsStore;

internal static class UserAnalyticsProcessMutations
{
    private const int BufferedPersistedCopies = 0;
    private const int LocalPersistedCopies = 1;
    internal static void Bootstrap(PartitionHost host, CancellationToken token)
    {
        var definition = new ResourceDefinition(P.Store, ResourceKind.AnalyticsStore, P.Domain)
        {
            AnalyticsSchema = new([P.Region], [P.Amount])
        };
        var configure = Issue(host, OperationKind.ConfigureResource,
            new ConfigureResourceRequest(P.Tenant, P.Database, definition), Guid.NewGuid(), token);
        UserAnalyticsProcessState.Equal(definition, host.Database.ApplyEmbedded(configure, token).Get<ResourceDefinition>());
    }

    internal static (Command<DatabaseCommand> Operation, CommitReceipt Receipt) Write(PartitionHost host, bool update,
        CancellationToken token)
    {
        var mutation = new UpsertAnalyticsEvents(P.Store,
            update ? [P.Event(P.First, P.UpdatedValue)] : [P.Event(P.First, P.FirstValue), P.Event(P.Second, P.SecondValue)],
            update ? ImmutableDictionary<string, long>.Empty.Add(P.First, P.Created)
                : ImmutableDictionary<string, long>.Empty.Add(P.First, P.Absent).Add(P.Second, P.Absent));
        var command = new CommandRequest(Guid.NewGuid(), P.Partition, [mutation]);
        var original = Issue(host, OperationKind.Batch, command, command.CommandId, token);
        var expected = new CommitReceipt(command.CommandId, new(host.Database.Store.Identity.Incarnation,
            P.Partition.AtomicPartitionId, checked(host.Database.LastApplied + P.Created)), [new(AnalyticsStoreProtocol.UpsertKind, P.Store, update ? P.First : P.Second, update ? P.Updated : P.Created)], host.Database.Durability,
            host.Database.Durability == DurabilityProfile.Buffered ? BufferedPersistedCopies : LocalPersistedCopies);
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

    private static Command<DatabaseCommand> Issue(PartitionHost host, OperationKind kind, object payload, Guid id, CancellationToken token)
    {
        var work = new ReadExecutionBudget(host.Database.EvaluationClock, token);
        return host.Database.CreateCommand(kind, id, PartitionStoreProtocol.AdministratorId,
            TimeProvider.System.GetUtcNow(), OrleansSerialization.Serialize(payload), work);
    }
}
