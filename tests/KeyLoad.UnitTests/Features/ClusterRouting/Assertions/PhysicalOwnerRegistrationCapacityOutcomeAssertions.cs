namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class PhysicalOwnerRegistrationCapacityOutcomeAssertions
{
    private const string Capacity = "The physical owner directory is full.";

    internal static async Task RefusedAsync(OperationResult result)
    {
        await Assert.That(result.Error).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(result.SafeDetail).IsEqualTo(Capacity);
        await Assert.That(result.Json).IsNull();
        await Assert.That(result.NativeValue).IsNull();
    }
}
