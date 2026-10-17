using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class D2ResourceInventoryPolicy
{
    internal static async Task ExecuteAsync(D2ResourceInventoryFixture fixture, D2ResourceInventorySeed seed,
        CancellationToken token)
    {
        var originalWork = fixture.Work(token);
        var control = fixture.Capture(seed.A, originalWork);
        var first = fixture.Page(control, seed.Observed, null, originalWork);
        var cursor = first.Continuation ?? throw new InvalidOperationException("The original D2 cursor is required.");
        await D2ResourceInventorySeeding.PolicyAsync(fixture, false, D2ResourceInventoryData.DemotedEpoch, token);
        var denied = ControlledPartitionMovementExpiryOwnerState.Capture(fixture.Owner);
        await D2ResourceInventoryRefusals.FailureAsync(() => fixture.Capture(seed.A, fixture.Work(token)), ErrorCode.PermissionDenied);
        await D2ResourceInventoryRefusals.FailureAsync(() =>
            fixture.Page(control, seed.Observed, cursor, fixture.Work(token)), ErrorCode.PermissionDenied);
        await D2ResourceInventoryRefusals.FailureAsync(() =>
            fixture.Database.ValidateResourceOwnerInventoryControl(D2ResourceInventoryData.Administrator,
                control, fixture.Work(token)), ErrorCode.PermissionDenied);
        await denied.AssertUnchangedAsync(fixture.Owner);
        await D2ResourceInventorySeeding.PolicyAsync(fixture, true, D2ResourceInventoryData.RepairedEpoch, token);
        var repaired = ControlledPartitionMovementExpiryOwnerState.Capture(fixture.Owner);
        await D2ResourceInventoryRefusals.FailureAsync(() =>
            fixture.Page(control, seed.Observed, cursor, fixture.Work(token)), ErrorCode.TokenInvalidated);
        await D2ResourceInventoryRefusals.FailureAsync(() =>
            fixture.Database.ValidateResourceOwnerInventoryControl(D2ResourceInventoryData.Administrator,
                control, fixture.Work(token)), ErrorCode.TokenInvalidated);
        await repaired.AssertUnchangedAsync(fixture.Owner);
        await D2ResourceInventoryAssertions.RowsAsync(fixture, seed, seed.A, token);
        await D2ResourceInventoryAssertions.RowsAsync(fixture, seed, seed.B, token);
        await D2ResourceInventoryCold.ReplayAsync(fixture, seed, token);
    }
}
