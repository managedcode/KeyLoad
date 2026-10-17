using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class PlacementCapacityEnrollmentFlow
{
    private const int ControlOwnerIndex = 0;
    private const long EpochStep = 1;
    private const int DirectoryReadRecords = 1;
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new PhysicalShardCatalogFixture(
                PhysicalOwnerDirectoryWholeFlow.Control.Owner.Incarnation,
                PhysicalOwnerDirectoryWholeFlow.Control.Owner);
            await ServerFailureObserver.ObserveAsync(() => ExecuteAsync(fixture), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(PhysicalShardCatalogFixture fixture)
    {
        PhysicalOwnerDirectoryWholeFlow.Bootstrap(fixture);
        var owners = Owners();
        await PlacementCapacityEnrollmentAssertions.RefusedAsync(fixture, owners);
        var original = PhysicalOwnerDirectoryWholeFlow.Operation(fixture,
            PhysicalOwnerDirectoryWholeFlow.Request(), PhysicalOwnerDirectoryWholeFlow.RegistrationId);
        var registered = fixture.Database.Apply(original);
        await Assert.That(registered.Error).IsNull();
        var image = PhysicalOwnerDirectoryWholeFlow.Image(fixture);
        var position = fixture.Store.Position;
        await PlacementCapacityEnrollmentAssertions.HealthyAsync(fixture, owners);
        await PlacementCapacityEnrollmentAssertions.UnchangedAsync(fixture, image, position);
        fixture.Reopen();
        await FencesAsync(fixture, owners, image, position);
        await Assert.That(fixture.Database.Apply(original).Error).IsNull();
        await PlacementCapacityEnrollmentAssertions.UnchangedAsync(fixture, image, position);
        fixture.Reopen();
        await FencesAsync(fixture, owners, image, position);
        await Assert.That(fixture.Database.Apply(original).Error).IsNull();
        await PlacementCapacityEnrollmentAssertions.UnchangedAsync(fixture, image, position);
    }

    private static async Task FencesAsync(PhysicalShardCatalogFixture fixture,
        ImmutableArray<PhysicalShardRecord> owners, string[] image, long position)
    {
        var destination = PhysicalOwnerDirectoryWholeFlow.Destination.Owner;
        await PlacementCapacityEnrollmentAssertions.RefusedAsync(fixture,
            [owners[ControlOwnerIndex], destination with { Incarnation = PhysicalOwnerDirectoryWholeFlow.Control.Owner.Incarnation }]);
        await PlacementCapacityEnrollmentAssertions.RefusedAsync(fixture,
            [owners[ControlOwnerIndex], destination with { PlacementEpoch = destination.PlacementEpoch + EpochStep }]);
        await PlacementCapacityEnrollmentAssertions.RefusedAsync(fixture,
            [owners[ControlOwnerIndex], destination with { VoterIds = destination.VoterIds.Reverse().ToImmutableArray() }]);
        await PlacementCapacityEnrollmentAssertions.RefusedAsync(fixture, [owners[ControlOwnerIndex]]);
        Admit(fixture, owners);
        await PlacementCapacityEnrollmentAssertions.UnchangedAsync(fixture, image, position);
    }

    private static ImmutableArray<PhysicalShardRecord> Owners()
        => [PhysicalOwnerDirectoryWholeFlow.Control.Owner, PhysicalOwnerDirectoryWholeFlow.Destination.Owner];

    internal static ReadExecutionBudget Admit(PhysicalShardCatalogFixture fixture,
        ImmutableArray<PhysicalShardRecord> owners)
    {
        var work = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(),
            fixture.Database.EvaluationClock, CancellationToken.None);
        work.ConstrainNativeWork(work.MaximumNativeReadBytes, DirectoryReadRecords);
        fixture.Store.Read(view =>
        {
            PlacementCapacityEnrollment.Require(work.CreateView(view),
                PhysicalOwnerDirectoryWholeFlow.Control.Owner, owners);
            work.Check();
            return true;
        });
        return work;
    }
}
