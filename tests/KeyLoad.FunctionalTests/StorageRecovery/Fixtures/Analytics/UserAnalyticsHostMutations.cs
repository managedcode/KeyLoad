using System.Collections.Immutable;
using KeyLoad.Server;
using ManagedCode.Communication.Commands;
using P = KeyLoad.FunctionalTests.StorageRecovery.UserAnalyticsHostProtocol;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal static class UserAnalyticsHostMutations
{
    internal static async Task BootstrapAsync(PartitionHost host, CancellationToken token)
    {
        var definition = new ResourceDefinition(P.Store, ResourceKind.AnalyticsStore, P.Domain)
        {
            AnalyticsSchema = new([P.Region], [P.Amount])
        };
        var configure = host.Database.CreateCommand(OperationKind.ConfigureResource, Guid.NewGuid(),
            PartitionStoreProtocol.AdministratorId, TimeProvider.System.GetUtcNow(), OrleansSerialization.Serialize(
                new ConfigureResourceRequest(P.Tenant, P.Database, definition)));
        await UserAnalyticsHostAssertions.SameAsync(definition,
            host.Database.ApplyEmbedded(configure, token).Get<ResourceDefinition>());
    }

    internal static async Task<(Command<DatabaseCommand> Original, CommitReceipt Receipt)> ApplyAsync(PartitionHost host,
        Mutation mutation, string kind, string id, long generation)
    {
        var work = new ReadExecutionBudget(host.Database.EvaluationClock,
            TestContext.Current!.Execution.CancellationToken);
        var command = new CommandRequest(Guid.NewGuid(), P.Partition, [mutation]);
        var operation = host.Database.CreateCommand(OperationKind.Batch, command.CommandId,
            PartitionStoreProtocol.AdministratorId, TimeProvider.System.GetUtcNow(), OrleansSerialization.Serialize(command), work);
        var expected = new CommitReceipt(command.CommandId, new(host.Database.Store.Identity.Incarnation,
            P.Partition.AtomicPartitionId, checked(host.Database.LastApplied + 1)), [new(kind, P.Store, id, generation)], DurabilityProfile.ProcessDurable, 1);
        var actual = host.Database.Apply(operation).Get<CommitReceipt>();
        await UserAnalyticsHostAssertions.SameAsync(expected, actual);
        await Assert.That(actual.Mutations.Single().CompositionReferences.IsEmpty).IsTrue();
        return (operation, expected);
    }

    internal static UpsertAnalyticsEvents Initial() => new(P.Store, [P.Event(P.First, 10d), P.Event(P.Second, 20d)],
        ImmutableDictionary<string, long>.Empty.Add(P.First, 0).Add(P.Second, 0));
    internal static UpsertAnalyticsEvents Update() => new(P.Store, [P.Event(P.First, 40d)],
        ImmutableDictionary<string, long>.Empty.Add(P.First, 1));
    internal static DeleteAnalyticsEvents Delete() => new(P.Store,
        ImmutableDictionary<string, long>.Empty.Add(P.Second, 1));
}
