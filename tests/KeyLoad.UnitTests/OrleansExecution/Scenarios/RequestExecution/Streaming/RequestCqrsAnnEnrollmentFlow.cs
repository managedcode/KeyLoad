using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Server.Search;

namespace KeyLoad.UnitTests.OrleansExecution;

internal static class RequestCqrsAnnEnrollmentFlow
{
    private const long InitialCheckpoint = 0;
    private const string ProjectionBatchPurpose = "projection-batch";

    internal static async Task<byte[]> ReserveAsync(RequestCqrsHostedRuntimeFixture fixture,
        AnnMaintenanceRequest original, CancellationToken token)
    {
        var configureId = AnnMaintenanceChildIdentity.Create(original.CommandId,
            AnnMaintenancePhase.Configure, InitialCheckpoint);
        var definition = new ProjectionConsumerDefinition(original.IndexGeneration, [],
            [MutationDiscriminatorNames.PutDocument, MutationDiscriminatorNames.PatchDocument,
             MutationDiscriminatorNames.DeleteDocument, MutationDiscriminatorNames.PutVector,
             MutationDiscriminatorNames.ApplyVectorProjection])
        {
            AnnMaintenance = original.MaintenanceProfile
        };
        var configure = new ConfigureProjectionConsumerRequest(configureId, original.Consumer, definition)
        {
            InitialAnnMaintenance = original
        };
        var configured = await RequestCqrsHostedMaintenanceData.ExecuteAsync<ProjectionConsumerInfo>(fixture.Database,
            OperationKind.ConfigureProjectionConsumer, configure, configureId, token);
        var principal = fixture.Database.Store.Read(view => fixture.Database.Database.Principal(view,
            RequestCqrsAnnTestData.Principal, fixture.Clock.GetUtcNow()));
        await RequestCqrsAnnAssertions.EqualAsync(new ProjectionConsumerInfo(original.Consumer, definition,
            InitialCheckpoint, false)
        {
            MaintenanceAuthority = new(RequestCqrsAnnTestData.Principal,
                configureId, fixture.Database.Store.Identity.Incarnation, principal.PolicyEpoch),
            InitialAnnMaintenance = original,
            InitialAnnMaintenanceSettled = false
        }, configured);
        await RequestCqrsAnnOriginalReplay.RefuseUnsettledAsync(fixture, original, token);
        var persisted = Current(fixture, original);
        await RequestCqrsAnnAssertions.EqualAsync(configured, persisted);
        return OrleansSerialization.Serialize(persisted.InitialAnnMaintenance
            ?? throw new InvalidOperationException());
    }

    internal static async Task RequireSettledAsync(RequestCqrsHostedRuntimeFixture fixture,
        AnnMaintenanceRequest original, AnnMaintenanceResult result, byte[] originalReservationBytes)
    {
        var current = Current(fixture, original);
        var authority = current.MaintenanceAuthority ?? throw new InvalidOperationException();
        await Assert.That(current.InitialAnnMaintenanceSettled).IsTrue();
        await Assert.That(current.Checkpoint).IsEqualTo(RequestCqrsAnnTestData.OriginalTail);
        await RequestCqrsAnnAssertions.EqualAsync(original, current.InitialAnnMaintenance);
        var root = Path.Combine(fixture.Database.Directory, AnnProtocol.RootDirectory);
        var receipt = AnnReceiptFiles.Read<AnnRootReceipt>(Path.Combine(root,
            AnnProtocol.RootReceipt), new AnnExecutionOptions()).Value;
        var retained = receipt.OriginalOperations.Single(value => value.Key.Consumer == original.Consumer
            && value.Key.IndexGeneration == original.IndexGeneration);
        await Assert.That(retained.PendingRequest).IsNull();
        await Assert.That(retained.PendingStarted).IsFalse();
        await Assert.That(retained.InitialSettled).IsTrue();
        await RequestCqrsAnnAssertions.EqualAsync(original, retained.InitialRequest);
        await RequestCqrsAnnAssertions.EqualAsync(original, retained.LastRequest);
        await RequestCqrsAnnAssertions.EqualAsync(result, retained.LastResult);
        var intent = retained.InitialSettlementIntent ?? throw new InvalidOperationException();
        var claims = fixture.Database.Database.Verify<AnnProjectionSettlementClaims>(
            intent.InitialAnnMaintenanceSettlement ?? throw new InvalidOperationException());
        var page = fixture.Database.Database.Verify<ProjectionBatchClaims>(intent.Token);
        var child = AnnMaintenanceChildIdentity.Create(original.CommandId, AnnMaintenancePhase.Completed,
            RequestCqrsAnnTestData.OriginalTail);
        await RequestCqrsAnnAssertions.EqualAsync(new CommitProjectionBatchRequest(child,
            original.Consumer, intent.Token, [])
        {
            InitialAnnMaintenanceSettlement = intent.InitialAnnMaintenanceSettlement
        }, intent);
        await RequestCqrsAnnAssertions.EqualAsync(new AnnProjectionSettlementClaims(
            AnnProjectionSettlementClaims.SettlementPurpose, fixture.Database.Store.Identity.Incarnation,
            RequestCqrsAnnTestData.Principal, authority.PolicyEpoch,
            AnnMaintenanceChildIdentity.Create(original.CommandId, AnnMaintenancePhase.Configure, InitialCheckpoint),
            original.Consumer, original.IndexGeneration, original.CommandId,
            SHA256.HashData(originalReservationBytes), SHA256.HashData(OrleansSerialization.Serialize(result)),
            RequestCqrsAnnTestData.OriginalTail, original.NodeId, original.Placement, page.ExpiresAt, child), claims);
        await RequestCqrsAnnAssertions.EqualAsync(new ProjectionBatchClaims(ProjectionBatchPurpose,
            fixture.Database.Store.Identity.Incarnation, original.Consumer, original.IndexGeneration,
            RequestCqrsAnnTestData.OriginalTail, RequestCqrsAnnTestData.OriginalTail, claims.ExpiresAt), page);
        await Assert.That(page.After).IsEqualTo(RequestCqrsAnnTestData.OriginalTail);
        await Assert.That(page.Through).IsEqualTo(RequestCqrsAnnTestData.OriginalTail);
        await Assert.That(page.Consumer).IsEqualTo(original.Consumer);
        await Assert.That(page.IndexGeneration).IsEqualTo(original.IndexGeneration);
        await Assert.That(page.Incarnation).IsEqualTo(fixture.Database.Store.Identity.Incarnation);
    }

    private static ProjectionConsumerInfo Current(RequestCqrsHostedRuntimeFixture fixture,
        AnnMaintenanceRequest request)
        => fixture.Database.Database.GetOutboxStatus(RequestCqrsAnnTestData.Principal,
            request.Consumer.Partition).Consumers.Single(row => row.Consumer == request.Consumer);
}
