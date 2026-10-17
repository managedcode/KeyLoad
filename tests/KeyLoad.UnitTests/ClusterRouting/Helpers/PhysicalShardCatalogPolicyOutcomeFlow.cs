using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class PhysicalShardCatalogPolicyOutcomeFlow
{
    internal const string PrincipalId = "catalog-policy-outcome-reader";
    private const string AdministrationRequired = "Cluster administration is required.";
    private const string TenantId = "system";
    private const long GrantedPolicyEpoch = 2;
    private static readonly Guid DeniedId = Guid.Parse("4f2b4b08-e690-4a82-8180-e51d994cf721");
    private static readonly Guid AuthorizedId = Guid.Parse("b8b7ef16-f798-40c8-8159-b818345de315");
    private static readonly Guid ContinuationId = Guid.Parse("7f353572-793a-42e0-a47e-9bf5b38b51cb");

    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new PhysicalShardCatalogFixture();
            await ServerFailureObserver.ObserveAsync(() => ExecuteAsync(fixture), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(PhysicalShardCatalogFixture fixture)
    {
        var principal = new PrincipalRecord(PrincipalId, TenantId, [], []);
        fixture.AddPrincipal(principal);
        var denied = Operation(fixture, DeniedId, Request());
        var refusal = fixture.Database.Apply(denied);
        await Assert.That(refusal.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(refusal.SafeDetail).IsEqualTo(AdministrationRequired);
        await Assert.That(refusal.Json).IsNull();
        await Assert.That(refusal.NativeValue).IsNull();
        await PhysicalShardCatalogPolicyOutcomeAssertions.AbsentAsync(fixture);
        var originalOutcome = PhysicalShardCatalogPolicyOutcomeAssertions.Outcome(fixture, DeniedId);
        var deniedImage = PhysicalShardCatalogPolicyOutcomeAssertions.Image(fixture);
        var deniedCut = fixture.Store.Position;
        fixture.Reopen();
        await PhysicalShardCatalogPolicyOutcomeAssertions.UnchangedAsync(fixture, deniedImage, deniedCut);
        await PhysicalShardCatalogPolicyOutcomeAssertions.StoredRefusalAsync(fixture, denied, refusal, originalOutcome);
        await GrantAndReplayAsync(fixture, principal, denied, originalOutcome);
        await AuthorizeFreshAndContinueAsync(fixture, denied, originalOutcome);
    }

    private static async Task GrantAndReplayAsync(PhysicalShardCatalogFixture fixture,
        PrincipalRecord principal, ReplicatedOperation denied, byte[] originalOutcome)
    {
        var granted = principal with
        {
            ClusterAdministrator = true,
            PolicyEpoch = GrantedPolicyEpoch
        };
        var result = fixture.Database.Apply(new(Guid.NewGuid(), OperationKind.ConfigurePrincipal,
            PhysicalShardCatalogFixture.RootPrincipalId, fixture.Database.EvaluationClock.GetUtcNow(),
            JsonSerializer.Serialize(new ConfigurePrincipalRequest(granted), JsonDefaults.Options)));
        await Assert.That(result.Error).IsNull();
        var persisted = fixture.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(PrincipalId)));
        await Assert.That(NativeSerialization.Serialize(persisted).SequenceEqual(NativeSerialization.Serialize(granted))).IsTrue();
        var image = PhysicalShardCatalogPolicyOutcomeAssertions.Image(fixture);
        var cut = fixture.Store.Position;
        await PhysicalShardCatalogPolicyOutcomeAssertions.PolicyReplayAsync(fixture, denied, originalOutcome);
        await PhysicalShardCatalogPolicyOutcomeAssertions.UnchangedAsync(fixture, image, cut);
        fixture.Reopen();
        await PhysicalShardCatalogPolicyOutcomeAssertions.UnchangedAsync(fixture, image, cut);
        await PhysicalShardCatalogPolicyOutcomeAssertions.PolicyReplayAsync(fixture, denied, originalOutcome);
        await PhysicalShardCatalogPolicyOutcomeAssertions.UnchangedAsync(fixture, image, cut);
        await PhysicalShardCatalogPolicyOutcomeAssertions.AbsentAsync(fixture);
    }

    private static async Task AuthorizeFreshAndContinueAsync(PhysicalShardCatalogFixture fixture,
        ReplicatedOperation denied, byte[] originalOutcome)
    {
        var authorized = Operation(fixture, AuthorizedId, Request());
        var result = fixture.Database.Apply(authorized);
        await Assert.That(result.Error).IsNull();
        await Assert.That(result.Get<bool>()).IsTrue();
        var receipt = NativeSerialization.Serialize(result);
        var image = PhysicalShardCatalogPolicyOutcomeAssertions.Image(fixture);
        var cut = fixture.Store.Position;
        await PhysicalShardCatalogPolicyOutcomeAssertions.HealthyAsync(fixture, authorized, receipt);
        await PhysicalShardCatalogPolicyOutcomeAssertions.PolicyReplayAsync(fixture, denied, originalOutcome);
        await PhysicalShardCatalogPolicyOutcomeAssertions.UnchangedAsync(fixture, image, cut);
        fixture.Reopen();
        await PhysicalShardCatalogPolicyOutcomeAssertions.UnchangedAsync(fixture, image, cut);
        await PhysicalShardCatalogPolicyOutcomeAssertions.HealthyAsync(fixture, authorized, receipt);
        var changed = Operation(fixture, AuthorizedId, Request() with
        {
            Incarnation = Guid.NewGuid()
        });
        await Assert.That(fixture.Database.Apply(changed).Error).IsEqualTo(ErrorCode.Conflict);
        await PhysicalShardCatalogPolicyOutcomeAssertions.UnchangedAsync(fixture, image, cut);
        var continuation = fixture.Database.Apply(Operation(fixture, ContinuationId, Request()));
        await Assert.That(continuation.Error).IsNull();
        await Assert.That(continuation.Get<bool>()).IsTrue();
        var continuedImage = PhysicalShardCatalogPolicyOutcomeAssertions.Image(fixture);
        var continuedCut = fixture.Store.Position;
        fixture.Reopen();
        await PhysicalShardCatalogPolicyOutcomeAssertions.UnchangedAsync(fixture, continuedImage, continuedCut);
        await PhysicalShardCatalogPolicyOutcomeAssertions.HealthyAsync(fixture, authorized, receipt);
        await PhysicalShardCatalogPolicyOutcomeAssertions.PolicyReplayAsync(fixture, denied, originalOutcome);
        await PhysicalShardCatalogPolicyOutcomeAssertions.UnchangedAsync(fixture, continuedImage, continuedCut);
    }

    internal static BootstrapPhysicalShardCatalogRequest Request()
        => PhysicalShardCatalogBootstrapTests.Request(PhysicalOwnerDirectoryWholeFlow.Control.Owner.PhysicalShardId,
            PhysicalOwnerDirectoryWholeFlow.Control.Owner.Incarnation, PhysicalShardCatalogVoterIds.Standard);

    private static ReplicatedOperation Operation(PhysicalShardCatalogFixture fixture, Guid id,
        BootstrapPhysicalShardCatalogRequest request)
        => fixture.Database.CreateNativeOperation(OperationKind.BootstrapPhysicalShardCatalog, id, PrincipalId,
            fixture.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
}
