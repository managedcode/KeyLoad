using System.Diagnostics;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class OrleansTelemetryAncestryFlow
{
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        RequestCqrsClusterFixture? fixture = null;
        OrleansRuntimeTelemetryFixture? telemetry = null;
        await ServerFailureObserver.ObserveAsync(() =>
        {
            fixture = new(nativeReplicaAdmission: true);
            return InitializeAsync(fixture);
        }, failures);
        if (failures.Count == 0)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                telemetry = await OrleansRuntimeTelemetryFixture.StartAsync();
                await ExecuteAsync(fixture!, telemetry);
            }, failures);
        }
        if (telemetry is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => telemetry.DisposeAsync().AsTask(), failures);
        }
        if (fixture is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.DisposeAsync().AsTask(), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static Task InitializeAsync(RequestCqrsClusterFixture fixture)
        => fixture.InitializeAsync(TestContext.Current!.Execution.CancellationToken);

    private static async Task ExecuteAsync(RequestCqrsClusterFixture fixture,
        OrleansRuntimeTelemetryFixture telemetry)
    {
        await Assert.That(telemetry.Options.MaximumParentLinks)
            .IsEqualTo(OrleansTelemetryAncestryProtocol.MaximumParentLinks);
        fixture.Database.Configure(OrleansTelemetryAncestryProtocol.Collection, ResourceKind.Collection);
        var principal = new PrincipalRecord(OrleansTelemetryAncestryProtocol.Principal,
            fixture.Database.Partition.TenantId,
            [new ScopeGrant(fixture.Database.Partition.DatabaseId, OrleansTelemetryAncestryProtocol.Collection,
                Capability.DocumentsWrite)], []);
        var persisted = fixture.Database.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
        await Assert.That(JsonDefaults.Serialize(persisted).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(principal))).IsTrue();
        List<string> documents = [];
        await PhaseAsync(fixture, telemetry, persisted, documents, OrleansTelemetryAncestryProtocol.DeepDocument,
            OrleansTelemetryAncestryProtocol.MaximumParentLinks, expectedRecorded: false);
        await PhaseAsync(fixture, telemetry, persisted, documents, OrleansTelemetryAncestryProtocol.BoundaryDocument,
            OrleansTelemetryAncestryProtocol.MaximumParentLinks - OrleansTelemetryAncestryProtocol.OneLink,
            expectedRecorded: true);
        await PhaseAsync(fixture, telemetry, persisted, documents, OrleansTelemetryAncestryProtocol.HealthyDocument,
            OrleansTelemetryAncestryProtocol.NoLocalParents, expectedRecorded: true);
    }

    private static async Task PhaseAsync(RequestCqrsClusterFixture fixture,
        OrleansRuntimeTelemetryFixture telemetry, PrincipalRecord principal, List<string> documents,
        string documentId, int localParents, bool expectedRecorded)
    {
        var original = Activity.Current;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => RunOwnedPhaseAsync(fixture, telemetry, principal,
            documents, documentId, localParents, expectedRecorded, failures), failures);
        await ServerFailureObserver.ObserveAsync(() => OrleansRuntimeTelemetryAssertions
            .AssertActivityRestoredAsync(original), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RunOwnedPhaseAsync(RequestCqrsClusterFixture fixture,
        OrleansRuntimeTelemetryFixture telemetry, PrincipalRecord principal, List<string> documents,
        string documentId, int localParents, bool expectedRecorded, List<Exception> failures)
    {
        using var chain = new OrleansTelemetryAncestryChain();
        OrleansTelemetryAncestryObservation? observation = null;
        await ServerFailureObserver.ObserveAsync(() =>
        {
            var parent = chain.Start(telemetry, localParents);
            observation = new(parent);
            return OrleansTelemetryAncestryOperation.RunAsync(fixture, principal, documentId, documents);
        }, failures);
        if (observation is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => observation.RequireAsync(telemetry, expectedRecorded), failures);
            ServerFailureObserver.Observe(observation.Dispose, failures);
        }
    }
}
