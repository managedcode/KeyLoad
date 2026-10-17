using KeyLoad.Core;
using KeyLoad.CrashHost.Features.ClusterRouting;

namespace KeyLoad.CrashHost.Features.ResourceExecution;

internal static class D2InventoryCrashCorpus
{
    private const long InitialOwnershipEpoch = 1;
    private const string SourcePhysicalShardIdText = "2c9d899d-bf37-4b8f-8fb9-036a159998ec";
    private const string SourceIncarnationText = "aa6746a6-220d-4ce2-82ce-4312e7530383";
    private const string TargetPhysicalShardIdText = "e12a6b3f-0ec6-4213-a668-e10a4dc78869";
    private const string TargetIncarnationText = "2d13b0cf-cbba-475b-bf90-130beb8c013c";
    private const string SourceFirstVoter = "http://127.0.0.1:22101";
    private const string SourceFirstEndpoint = "http://127.0.0.1:22101/";
    private const string SourceSecondVoter = "http://127.0.0.1:22102";
    private const string SourceSecondEndpoint = "http://127.0.0.1:22102/";
    private const string SourceThirdVoter = "http://127.0.0.1:22103";
    private const string SourceThirdEndpoint = "http://127.0.0.1:22103/";
    private const string TargetFirstVoter = "http://127.0.0.1:22104";
    private const string TargetFirstEndpoint = "http://127.0.0.1:22104/";
    private const string TargetSecondVoter = "http://127.0.0.1:22105";
    private const string TargetSecondEndpoint = "http://127.0.0.1:22105/";
    private const string TargetThirdVoter = "http://127.0.0.1:22106";
    private const string TargetThirdEndpoint = "http://127.0.0.1:22106/";
    private const string FreshBootstrapRequired = "The genuine fresh writer has more than its authentication bootstrap commit.";
    private const string NativeCommandFailedPrefix = "The genuine D2 native fixture command failed: ";
    internal static readonly RegisteredPhysicalOwnerV1 Source = new(new(
        Guid.Parse(SourcePhysicalShardIdText),
        Guid.Parse(SourceIncarnationText),
        [SourceFirstVoter, SourceSecondVoter, SourceThirdVoter], InitialOwnershipEpoch),
        [SourceFirstEndpoint, SourceSecondEndpoint, SourceThirdEndpoint]);
    internal static readonly RegisteredPhysicalOwnerV1 Target = new(new(
        Guid.Parse(TargetPhysicalShardIdText),
        Guid.Parse(TargetIncarnationText),
        [TargetFirstVoter, TargetSecondVoter, TargetThirdVoter], InitialOwnershipEpoch),
        [TargetFirstEndpoint, TargetSecondEndpoint, TargetThirdEndpoint]);

    internal static D2InventoryCrashEvidence Seed(ControlledPartitionMovementNativeNode source,
        ControlledPartitionMovementNativeNode target, CancellationToken token)
    {
        Bootstrap(source, Source, D2InventoryCrashProtocol.SourceBootstrapId, token);
        Bootstrap(target, Target, D2InventoryCrashProtocol.TargetBootstrapId, token);
        Submit(source, OperationKind.RegisterPhysicalOwner, D2InventoryCrashProtocol.RegistrationId,
            new RegisterPhysicalOwnerV1(D2InventoryCrashProtocol.Version, D2InventoryCrashProtocol.EmptyRevision,
                Source, Target), token);
        Configure(source, D2InventoryCrashProtocol.ResourceA, D2InventoryCrashProtocol.Domain, D2InventoryCrashProtocol.AId, token);
        Configure(source, D2InventoryCrashProtocol.ResourceB, D2InventoryCrashProtocol.Domain, D2InventoryCrashProtocol.BId, token);
        _ = source.Journal.Submit(Write(source.Database, D2InventoryCrashProtocol.P0Id, D2InventoryCrashProtocol.P0), token).Get<CommitReceipt>();
        var prefix = Write(source.Database, D2InventoryCrashProtocol.P1Id, D2InventoryCrashProtocol.P1);
        var receipt = source.Journal.Submit(prefix, token).Get<CommitReceipt>();
        var lastCommitted = Configure(source, D2InventoryCrashProtocol.ResourceC, D2InventoryCrashProtocol.OtherDomain, D2InventoryCrashProtocol.CId, token);
        var selected = Write(source.Database, D2InventoryCrashProtocol.SelectedId, D2InventoryCrashProtocol.P2);
        return new(selected, prefix, receipt,
            checked(source.Journal.Log.State.LastIndex + D2InventoryCrashProtocol.IndexStep), source.Store.Position, lastCommitted);
    }

    internal static ReplicatedOperation Write(DatabaseEngine database, Guid id, string partition)
        => database.CreateNativeOperation(OperationKind.Batch, id, D2InventoryCrashProtocol.Principal,
            database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(new CommandRequest(id,
                D2InventoryCrashProtocol.Partition(partition),
                [new PutDocument(D2InventoryCrashProtocol.ResourceA, D2InventoryCrashProtocol.DocumentId, D2InventoryCrashProtocol.Json)])));

    private static void Bootstrap(ControlledPartitionMovementNativeNode node, RegisteredPhysicalOwnerV1 owner,
        Guid id, CancellationToken token)
    {
        if (node.Store.Position != D2InventoryCrashProtocol.InitialRevision)
        {
            throw new InvalidOperationException(FreshBootstrapRequired);
        }
        Submit(node, OperationKind.BootstrapPhysicalShardCatalog, id,
            new BootstrapPhysicalShardCatalogRequest(D2InventoryCrashProtocol.Version, D2InventoryCrashProtocol.EmptyRevision,
                owner.Owner.PhysicalShardId, owner.Owner.Incarnation, owner.Owner.VoterIds), token);
    }

    private static ReplicatedOperation Configure(ControlledPartitionMovementNativeNode node, string resource,
        string domain, Guid id, CancellationToken token)
        => Submit(node, OperationKind.ConfigureResource, id,
            new ConfigureResourceRequest(D2InventoryCrashProtocol.Tenant, D2InventoryCrashProtocol.Database,
                new(resource, ResourceKind.Collection, domain)), token);

    private static ReplicatedOperation Submit<T>(ControlledPartitionMovementNativeNode node, OperationKind kind, Guid id,
        T request, CancellationToken token)
    {
        var operation = node.Database.CreateNativeOperation(kind, id, D2InventoryCrashProtocol.Principal,
            node.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
        var result = node.Journal.Submit(operation, token);
        if (result.Error is not null)
        {
            throw new InvalidOperationException(NativeCommandFailedPrefix + result.Error);
        }
        return operation;
    }
}
