using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.CrashHost.Features.ResourceExecution;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

internal static class D2ImportSeed
{
    private const string UnknownOriginPrincipal = "d2-before-catalog-no-grants";
    private const long AuthenticationAndPrincipalPosition = 2;
    internal static void Execute(D2ImportSession session, CancellationToken token, bool unknownTargetOrigin = false)
    {
        Bootstrap(session.Source, session.Owners.Control.Owner, token);
        if (unknownTargetOrigin)
        {
            Submit(session.Target, OperationKind.ConfigurePrincipal,
                new ConfigurePrincipalRequest(new(UnknownOriginPrincipal, D2ImportCrashProtocol.Tenant, [], [])), token);
            if (session.Target.Store.Position != AuthenticationAndPrincipalPosition)
            {
                throw new InvalidOperationException("The genuine pre-catalog principal commit did not advance the authentication-only cut.");
            }
        }
        Bootstrap(session.Target, session.Owners.Destination.Owner, token,
            unknownTargetOrigin ? AuthenticationAndPrincipalPosition : D2ImportCrashProtocol.Single);
        Submit(session.Source, OperationKind.RegisterPhysicalOwner,
            new RegisterPhysicalOwnerV1(D2ImportCrashProtocol.Version, D2ImportCrashProtocol.Empty, session.Owners.Control, session.Owners.Destination), token);
        Submit(session.Target, OperationKind.RegisterPhysicalOwner,
            new RegisterPhysicalOwnerV1(D2ImportCrashProtocol.Version, 0, session.Owners.Destination, session.Owners.Control), token);
        Configure(session.Source, D2ImportCrashProtocol.Collection, D2ImportCrashProtocol.Domain, token);
        Configure(session.Target, D2ImportCrashProtocol.LocalCollection, D2ImportCrashProtocol.LocalDomain, token);
        foreach (var key in new[] { D2ImportCrashProtocol.FirstPartition, D2ImportCrashProtocol.SecondPartition })
        {
            var original = Write(session.Source, D2ImportCrashProtocol.Partition(key),
                D2ImportCrashProtocol.Collection, D2ImportCrashProtocol.SourceJson);
            var result = session.Source.Journal.Submit(original, token);
            _ = result.Get<CommitReceipt>();
            session.SourceSeeds.Add((original, result));
        }
    }

    internal static ReplicatedOperation Write(ControlledPartitionMovementNativeNode node, PartitionRef partition,
        string collection, string json)
    {
        var id = Guid.NewGuid();
        return node.Database.CreateNativeOperation(OperationKind.Batch, id, D2ImportCrashProtocol.Principal,
            node.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(new CommandRequest(id, partition,
                [new PutDocument(collection, D2ImportCrashProtocol.DocumentId, json)])));
    }

    private static void Bootstrap(ControlledPartitionMovementNativeNode node, PhysicalShardRecord owner, CancellationToken token,
        long expectedPosition = D2ImportCrashProtocol.Single)
    {
        if (node.Store.Position != expectedPosition)
        {
            throw new InvalidOperationException("The new import owner is not at its sole genuine authentication bootstrap.");
        }
        Submit(node, OperationKind.BootstrapPhysicalShardCatalog, new BootstrapPhysicalShardCatalogRequest(
            D2ImportCrashProtocol.Version, 0, owner.PhysicalShardId, owner.Incarnation, owner.VoterIds), token);
    }

    private static void Configure(ControlledPartitionMovementNativeNode node, string name, string domain, CancellationToken token)
        => Submit(node, OperationKind.ConfigureResource, new ConfigureResourceRequest(D2ImportCrashProtocol.Tenant,
            D2ImportCrashProtocol.Database, new(name, ResourceKind.Collection, domain)), token);

    private static void Submit<T>(ControlledPartitionMovementNativeNode node, OperationKind kind, T body, CancellationToken token)
    {
        var operation = node.Database.CreateNativeOperation(kind, Guid.NewGuid(), D2ImportCrashProtocol.Principal,
            node.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(body));
        ControlledPartitionMovementProcessSetup.Require(node.Journal.Submit(operation, token));
    }
}
