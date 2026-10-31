using System.Collections.Immutable;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.FunctionalTests.Authorization;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;
using A = KeyLoad.FunctionalTests.DatabaseComposition.CompositionLinkLifetimeJsonAssertions;
using P = KeyLoad.FunctionalTests.DatabaseComposition.CompositionLinkLifetimeJsonProtocol;

namespace KeyLoad.FunctionalTests.DatabaseComposition;

internal static class CompositionLinkLifetimeJsonSeed
{
    private const string IdColumn = "id";

    internal static async Task<CompositionLinkLifetimeJsonState> CreateAsync(SafeProjectorOperationDatabase owner, CancellationToken token)
    {
        foreach (var name in new[] { P.From, P.To })
        {
            var definition = new ResourceDefinition(name, ResourceKind.Collection, owner.Source.Partition.TransactionDomainId)
            {
                RelationalSchema = new(IdColumn, [new(IdColumn, RelationalColumnType.Text), new("name", RelationalColumnType.Text)])
            };
            await A.EqualAsync(definition, owner.Source.Submit(OperationKind.ConfigureResource,
                new ConfigureResourceRequest(owner.Source.Partition.TenantId, owner.Source.Partition.DatabaseId, definition))
                .Get<ResourceDefinition>());
        }
        owner.Source.Configure(P.Graph, ResourceKind.Graph);
        owner.Source.Configure(P.Queue, ResourceKind.WorkQueue);
        owner.Source.Configure(P.HealthyQueue, ResourceKind.WorkQueue);
        var from = Reference(owner, P.From, P.First);
        var to = Reference(owner, P.To, P.Second);
        var seed = Issue(owner, [new PutDocument(P.From, P.First, P.FirstJson, 0),
            new PutDocument(P.To, P.Second, P.SecondJson, 0)], token);
        await A.ReceiptAsync(new(seed.CommandId, owner.NextToken(), [new("putDocument", P.From, P.First, 1), new("putDocument", P.To, P.Second, 1)], DurabilityProfile.ProcessDurable, 1),
            owner.Submit(seed).Get<CommitReceipt>());
        return new(from, to, seed);
    }

    internal static Command<DatabaseCommand> Issue(SafeProjectorOperationDatabase owner, ImmutableArray<Mutation> mutations,
        CancellationToken token)
    {
        var request = new CommandRequest(Guid.NewGuid(), owner.Source.Partition, mutations);
        var work = new ReadExecutionBudget(owner.Database.EvaluationClock, token);
        return owner.Database.CreateCommand(OperationKind.Batch, request.CommandId, P.Root,
            owner.Database.EvaluationClock.GetUtcNow(), request, work);
    }

    private static EntityRef Reference(SafeProjectorOperationDatabase owner, string name, string id)
        => owner.Store.Read(view =>
        {
            var partition = owner.Source.Partition;
            var definition = view.GetRecord<ResourceDefinition>(KeySpace.Resource(partition.TenantId, partition.DatabaseId, name));
            var lifetime = (ResourceLifetimeCatalog.Read(view, partition.TenantId, partition.DatabaseId, name, definition)).Value
                ?? throw new InvalidOperationException("The genuine configured endpoint has no resource lifetime.");
            return new EntityRef(partition, name, id) { Lifetime = new(lifetime.Incarnation.Value) };
        });
}
