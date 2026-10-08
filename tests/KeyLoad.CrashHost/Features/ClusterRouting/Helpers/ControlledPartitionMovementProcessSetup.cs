
namespace KeyLoad.CrashHost.Features.ClusterRouting;

/// <summary>Publishes actual local catalogs and original mixed data through native durable apply.</summary>
internal static class ControlledPartitionMovementProcessSetup
{
    private const string RegistrationIdText = "90705c41-9dfa-4c9a-9230-0d7a5d377f21";
    private const int Version = 1;
    private const long EmptyRevision = 0;
    private const string PrincipalId = "root";
    private const string Failed = "The original movement process native setup failed.";
    private static readonly Guid RegistrationId = Guid.Parse(RegistrationIdText);

    internal static (OperationResult Outcome, DateTimeOffset EvaluatedAt) Seed(
        ControlledPartitionMovementNativeNode source, ControlledPartitionMovementNativeNode destination,
        ControlledPartitionMovementProcessOwners owners, CancellationToken cancellationToken)
    {
        Bootstrap(source, owners.Control.Owner, cancellationToken);
        Bootstrap(destination, owners.Destination.Owner, cancellationToken);
        var request = new RegisterPhysicalOwnerV1(Version, EmptyRevision, owners.Control, owners.Destination);
        var original = source.Database.CreateNativeOperation(OperationKind.RegisterPhysicalOwner, RegistrationId,
            PrincipalId, source.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
        Require(source.Journal.Submit(original, cancellationToken));
        return ControlledPartitionMovementProcessSeed.Submit(source, cancellationToken);
    }

    private static void Bootstrap(ControlledPartitionMovementNativeNode node, PhysicalShardRecord owner,
        CancellationToken cancellationToken)
    {
        var request = new BootstrapPhysicalShardCatalogRequest(Version, EmptyRevision,
            owner.PhysicalShardId, owner.Incarnation, owner.VoterIds);
        var original = node.Database.CreateNativeOperation(OperationKind.BootstrapPhysicalShardCatalog,
            PhysicalShardCatalogIdentity.CreateBootstrapCommandId(owner.PhysicalShardId), PrincipalId,
            node.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
        Require(node.Journal.Submit(original, cancellationToken));
    }

    internal static void Require(OperationResult result)
    {
        if (result.Error is not null)
        { throw new InvalidOperationException(Failed); }
    }
}
