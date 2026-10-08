using KeyLoad.Orleans;
using KeyLoad.Server;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeAnnPersistedAdminTests
{
    private const int Count = 3;
    private const string OrdinaryId = "ann-ordinary";

    [Test]
    public async Task ActualPersistedOrdinaryPrincipalCannotBeginMaintenanceAndAdministratorPublishesHealthy()
    {
        using var database = AnnSeedTestSupport.Create(Count);
        var request = NativeAnnMaintenanceTestData.Pin(database);
        AnnProjectionPinTestSupport.ConfigurePrincipal(database,
            AnnProjectionPinTestSupport.OrdinaryPrincipal(OrdinaryId, database.Partition.TenantId));
        var failures = new List<Exception>();
        try
        {
            await using var runtime = new NativeAnnMaintenanceTestRuntime(database);
            var actual = runtime;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var before = NativeAnnMaintenanceTestData.Snapshot(database);
                var position = database.Store.Position;
                var principal = database.Store.Read(view => database.Database.Principal(view, OrdinaryId,
                    database.Database.EvaluationClock.GetUtcNow()));
                var denial = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => actual.Owner.ExecuteAsync(principal,
                    new(request, actual.SessionId, AnnMaintenanceCapabilityKind.Begin, null, null), TestContext.Current!.Execution.CancellationToken))
                    ?? throw new InvalidOperationException("The actual persisted non-administrator was not rejected.");
                await Assert.That(denial.Code).IsEqualTo(ErrorCode.PermissionDenied);
                await Assert.That(NativeAnnMaintenanceTestData.Snapshot(database)).IsEquivalentTo(before, CollectionOrdering.Matching);
                await Assert.That(database.Store.Position).IsEqualTo(position);
                var began = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, actual, request, AnnMaintenanceCapabilityKind.Begin);
                var healthy = await NativeAnnMaintenancePhaseAssertions.FinishAsync(database, actual, request, began.Source!.ThroughSequence);
                await Assert.That(healthy.Count).IsEqualTo(Count);
                await Assert.That(healthy.IndexSha256).IsNotNull();
            }, failures);
        }
        catch (Exception cleanup) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
