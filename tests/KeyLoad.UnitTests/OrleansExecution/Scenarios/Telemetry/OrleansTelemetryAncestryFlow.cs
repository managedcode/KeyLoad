using System.Diagnostics;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.OrleansExecution;

internal static class OrleansTelemetryAncestryFlow
{
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        RequestCqrsHostedRuntimeFixture? fixture = null;
        OrleansRuntimeTelemetryFixture? telemetry = null;
        await ServerFailureObserver.ObserveAsync(() =>
        {
            fixture = new();
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

    private static Task InitializeAsync(RequestCqrsHostedRuntimeFixture fixture)
        => fixture.InitializeAsync(TestContext.Current!.Execution.CancellationToken);

    private static async Task ExecuteAsync(RequestCqrsHostedRuntimeFixture fixture,
        OrleansRuntimeTelemetryFixture telemetry)
    {
        await Assert.That(telemetry.Options.MaximumParentLinks)
            .IsEqualTo(OrleansTelemetryAncestryProtocol.MaximumParentLinks);
        var creation = Guid.NewGuid();
        fixture.Database.Configure(OrleansTelemetryAncestryProtocol.Collection, ResourceKind.Collection, id: creation);
        var principal = new PrincipalRecord(OrleansTelemetryAncestryProtocol.Principal,
            fixture.Database.Partition.TenantId,
            [new ScopeGrant(fixture.Database.Partition.DatabaseId, OrleansTelemetryAncestryProtocol.Collection,
                Capability.DocumentsWrite)], []);
        var persisted = fixture.Database.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
        await Assert.That(JsonDefaults.Serialize(persisted).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(principal))).IsTrue();
        List<string> documents = [];
        await PhaseAsync(fixture, telemetry, persisted, creation, documents, OrleansTelemetryAncestryProtocol.DeepDocument,
            OrleansTelemetryAncestryProtocol.MaximumParentLinks, expectedRecorded: false);
        await PhaseAsync(fixture, telemetry, persisted, creation, documents, OrleansTelemetryAncestryProtocol.BoundaryDocument,
            OrleansTelemetryAncestryProtocol.MaximumParentLinks - OrleansTelemetryAncestryProtocol.OneLink,
            expectedRecorded: true);
        await PhaseAsync(fixture, telemetry, persisted, creation, documents, OrleansTelemetryAncestryProtocol.HealthyDocument,
            OrleansTelemetryAncestryProtocol.NoLocalParents, expectedRecorded: true);
    }

    private static async Task PhaseAsync(RequestCqrsHostedRuntimeFixture fixture,
        OrleansRuntimeTelemetryFixture telemetry, PrincipalRecord principal, Guid creation, List<string> documents,
        string documentId, int localParents, bool expectedRecorded)
    {
        var original = Activity.Current;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => RunOwnedPhaseAsync(fixture, telemetry, principal, creation,
            documents, documentId, localParents, expectedRecorded, failures), failures);
        await ServerFailureObserver.ObserveAsync(() => OrleansRuntimeTelemetryAssertions
            .AssertActivityRestoredAsync(original), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RunOwnedPhaseAsync(RequestCqrsHostedRuntimeFixture fixture,
        OrleansRuntimeTelemetryFixture telemetry, PrincipalRecord principal, Guid creation, List<string> documents,
        string documentId, int localParents, bool expectedRecorded, List<Exception> failures)
    {
        using var chain = new OrleansTelemetryAncestryChain();
        OrleansTelemetryAncestryObservation? observation = null;
        await ServerFailureObserver.ObserveAsync(() =>
        {
            var parent = chain.Start(telemetry, localParents);
            observation = new(parent);
            return OrleansTelemetryAncestryOperation.RunAsync(fixture, principal, creation, documentId, documents);
        }, failures);
        if (observation is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => observation.RequireAsync(telemetry, expectedRecorded), failures);
            ServerFailureObserver.Observe(observation.Dispose, failures);
        }
    }
}
