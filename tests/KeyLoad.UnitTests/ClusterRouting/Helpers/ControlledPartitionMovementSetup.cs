namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Creates actual empty destination infrastructure and authoritative source directory through native apply.</summary>
internal static class ControlledPartitionMovementSetup
{
    private const int Version = 1;
    private const long EmptyRevision = 0;
    private const string BootstrapFailure = "The native movement owner catalog bootstrap failed.";
    private const string RegistrationFailure = "The native movement source directory registration failed.";

    internal static OperationResult Seed(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode destination, out DateTimeOffset evaluatedAt, CancellationToken cancellationToken)
    {
        Bootstrap(source, PhysicalOwnerDirectoryWholeFlow.Control.Owner, cancellationToken);
        Bootstrap(destination, PhysicalOwnerDirectoryWholeFlow.Destination.Owner, cancellationToken);
        var registration = source.Database.CreateNativeOperation(OperationKind.RegisterPhysicalOwner,
            PhysicalOwnerDirectoryWholeFlow.RegistrationId, PhysicalShardCatalogFixture.RootPrincipalId,
            source.Database.EvaluationClock.GetUtcNow(),
            NativeSerialization.Serialize(PhysicalOwnerDirectoryWholeFlow.Request()));
        Require(source.Journal.Submit(registration, cancellationToken), RegistrationFailure);
        return ControlledPartitionMovementSeed.Submit(source.Database, source.Journal, out evaluatedAt, cancellationToken);
    }

    internal static OperationResult SeedConfigured(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode destination, ControlledPartitionMovementLoopbackCorpus corpus,
        out DateTimeOffset evaluatedAt, CancellationToken cancellationToken)
    {
        Bootstrap(source, corpus.Control.Owner, cancellationToken);
        Bootstrap(destination, corpus.Destination.Owner, cancellationToken);
        var request = new RegisterPhysicalOwnerV1(Version, EmptyRevision, corpus.Control, corpus.Destination);
        var registration = source.Database.CreateNativeOperation(OperationKind.RegisterPhysicalOwner,
            PhysicalOwnerDirectoryWholeFlow.RegistrationId, PhysicalShardCatalogFixture.RootPrincipalId,
            source.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
        Require(source.Journal.Submit(registration, cancellationToken), RegistrationFailure);
        return ControlledPartitionMovementSeed.Submit(source.Database, source.Journal, out evaluatedAt, cancellationToken);
    }

    private static void Bootstrap(ControlledPartitionMovementNode node, PhysicalShardRecord owner,
        CancellationToken cancellationToken)
    {
        var request = new BootstrapPhysicalShardCatalogRequest(Version, EmptyRevision,
            owner.PhysicalShardId, owner.Incarnation, owner.VoterIds);
        var operation = node.Database.CreateNativeOperation(OperationKind.BootstrapPhysicalShardCatalog,
            PhysicalShardCatalogIdentity.CreateBootstrapCommandId(owner.PhysicalShardId),
            PhysicalShardCatalogFixture.RootPrincipalId, node.Database.EvaluationClock.GetUtcNow(),
            NativeSerialization.Serialize(request));
        Require(node.Journal.Submit(operation, cancellationToken), BootstrapFailure);
    }

    private static void Require(OperationResult result, string safeFixtureFailure)
    {
        if (result.Error is not null)
        {
            throw new InvalidOperationException(safeFixtureFailure);
        }
    }
}
