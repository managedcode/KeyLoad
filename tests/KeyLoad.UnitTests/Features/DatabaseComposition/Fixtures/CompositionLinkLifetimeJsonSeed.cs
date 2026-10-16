using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.Authorization;
using A = KeyLoad.UnitTests.Features.DatabaseComposition.CompositionLinkLifetimeJsonAssertions;
using P = KeyLoad.UnitTests.Features.DatabaseComposition.CompositionLinkLifetimeJsonProtocol;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal static class CompositionLinkLifetimeJsonSeed
{
    internal static async Task<CompositionLinkLifetimeJsonState> CreateAsync(SafeProjectorOperationDatabase owner, CancellationToken token)
    {
        foreach (var name in new[] { P.From, P.To })
        {
            var definition = new ResourceDefinition(name, ResourceKind.Collection, owner.Source.Partition.TransactionDomainId)
            {
                RelationalSchema = new("id", [new("id", RelationalColumnType.Text), new("name", RelationalColumnType.Text)])
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
        await A.ReceiptAsync(new(seed.Id, owner.NextToken(),
            [new("putDocument", P.From, P.First, 1), new("putDocument", P.To, P.Second, 1)], DurabilityProfile.ProcessDurable),
            owner.Submit(seed).Get<CommitReceipt>());
        return new(from, to, seed);
    }

    internal static ReplicatedOperation Issue(SafeProjectorOperationDatabase owner, ImmutableArray<Mutation> mutations,
        CancellationToken token)
    {
        var request = new CommandRequest(Guid.NewGuid(), owner.Source.Partition, mutations);
        var work = new ReadExecutionBudget(owner.Database.OperationLimitsOptions, owner.Database.EvaluationClock, token);
        return owner.Database.CreateNativeOperation(OperationKind.Batch, request.CommandId, P.Root,
            owner.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request), work);
    }

    private static EntityRef Reference(SafeProjectorOperationDatabase owner, string name, string id)
        => owner.Store.Read(view =>
        {
            var partition = owner.Source.Partition;
            var definition = view.GetRecord<ResourceDefinition>(KeySpace.Resource(partition.TenantId, partition.DatabaseId, name));
            var lifetime = ResourceLifetimeCatalog.Read(view, partition.TenantId, partition.DatabaseId, name, definition)
                ?? throw new InvalidOperationException("The genuine configured endpoint has no resource lifetime.");
            return new EntityRef(partition, name, id) { Lifetime = new(lifetime.Incarnation.Value) };
        });
}
