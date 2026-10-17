using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class D2ResourceInventoryRefusals
{
    internal static async Task ExecuteAsync(D2ResourceInventoryFixture fixture, D2ResourceInventorySeed seed,
        CancellationToken token)
    {
        var originalWork = fixture.Work(token);
        var control = fixture.Capture(seed.A, originalWork);
        var page = fixture.Page(control, seed.Observed, null, originalWork);
        var cursor = page.Continuation ?? throw new InvalidOperationException("The genuine D2 page must continue.");
        var before = ControlledPartitionMovementExpiryOwnerState.Capture(fixture.Owner);
        await FailureAsync(() => fixture.Page(control, D2ResourceInventoryData.Destination, null,
            fixture.Work(token)), ErrorCode.OwnershipLost);
        await FailureAsync(() => fixture.Page(control, seed.Observed,
            cursor with
            {
                Incarnation = seed.B.Incarnation
            }, fixture.Work(token)), ErrorCode.TokenInvalidated);
        await FailureAsync(() => fixture.Page(control, seed.Observed,
            cursor with
            {
                InventoryId = Guid.NewGuid()
            }, fixture.Work(token)), ErrorCode.TokenInvalidated);
        await FailureAsync(() => fixture.Page(control, seed.Observed,
            cursor with
            {
                AfterKey = new byte[] { 0 }
            }, fixture.Work(token)), ErrorCode.Validation);
        await FailureAsync(() => fixture.Database.CaptureResourceOwnerInventoryControl(D2ResourceInventoryData.Administrator,
            control.Request with
            {
                ExpectedIncarnation = seed.B.Incarnation
            }, fixture.Work(token)), ErrorCode.TokenInvalidated);
        await FailureAsync(() => fixture.Database.CaptureResourceOwnerInventoryControl(D2ResourceInventoryData.Administrator,
            control.Request with
            {
                PageSize = D2ResourceInventoryData.InvalidPageSize
            }, fixture.Work(token)), ErrorCode.Validation);
        await FailureAsync(() => fixture.Database.CaptureResourceOwnerInventoryControl(D2ResourceInventoryData.Administrator,
            control.Request with
            {
                ExpiresAt = fixture.Database.EvaluationClock.GetUtcNow().AddTicks(D2ResourceInventoryData.BeforeExpiryTick)
            },
            fixture.Work(token)), ErrorCode.CursorExpired);
        await ActualBudgetsAsync(fixture, seed, token);
        await OriginalCancellationAsync(fixture, seed, token);
        await before.AssertUnchangedAsync(fixture.Owner);
        await D2ResourceInventoryAssertions.RowsAsync(fixture, seed, seed.A, token);
    }

    private static async Task ActualBudgetsAsync(D2ResourceInventoryFixture fixture, D2ResourceInventorySeed seed,
        CancellationToken token)
    {
        var records = fixture.Work(token);
        var recordControl = fixture.Capture(seed.A, records);
        var examined = records.ConstrainedNativeRecords;
        records.ConstrainNativeWork(records.MaximumNativeReadBytes, checked(examined + D2ResourceInventoryData.MinimalBudget));
        await FailureAsync(() => fixture.Page(recordControl, seed.Observed, null, records), ErrorCode.BudgetExceeded);
        await Assert.That(records.ConstrainedNativeRecords).IsGreaterThan(examined);
        var bytes = fixture.Work(token);
        var byteControl = fixture.Capture(seed.A, bytes);
        bytes.ConstrainNativeWork(checked(bytes.ReadBytes + D2ResourceInventoryData.MinimalBudget), bytes.MaximumNativeScanRecords);
        await FailureAsync(() => fixture.Page(byteControl, seed.Observed, null, bytes), ErrorCode.BudgetExceeded);
        var output = fixture.Work(token);
        var outputControl = fixture.Capture(seed.A, output);
        output.ConstrainResultBytes(D2ResourceInventoryData.MinimalBudget);
        await FailureAsync(() => fixture.Page(outputControl, seed.Observed, null, output), ErrorCode.BudgetExceeded);
    }

    private static async Task OriginalCancellationAsync(D2ResourceInventoryFixture fixture,
        D2ResourceInventorySeed seed, CancellationToken token)
    {
        using var selected = CancellationTokenSource.CreateLinkedTokenSource(token);
        var work = fixture.Work(selected.Token);
        var control = fixture.Capture(seed.A, work);
        var first = fixture.Page(control, seed.Observed, null, work);
        var cursor = first.Continuation ?? throw new InvalidOperationException("The original D2 cursor is required.");
        await selected.CancelAsync();
        _ = Assert.ThrowsExactly<OperationCanceledException>(() =>
            fixture.Page(control, seed.Observed, cursor, work));
    }

    internal static async Task FailureAsync(Action operation, ErrorCode expected)
        => await Assert.That(Assert.ThrowsExactly<KeyLoadException>(operation).Code).IsEqualTo(expected);
}
