namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class C1OutcomeInspectionLockRelease
{
    private const string StoreOwnerName = "owner.lock";
    internal static void Verify(C1OutcomeInspectionFixture fixture)
    {
        if (File.Exists(fixture.OuterOwnerLockPath))
        {
            using var outer = C1OutcomeInspectionOwnerPhase.Acquire(fixture,
                C1OutcomeInspectionOwnerRole.FinalOuter, fixture.OuterOwnerLockPath);
        }
        var storeOwnerPath = Path.Combine(fixture.DirectoryPath, StoreOwnerName);
        if (File.Exists(storeOwnerPath))
        {
            using var inner = C1OutcomeInspectionOwnerPhase.Acquire(fixture,
                C1OutcomeInspectionOwnerRole.FinalDatabase, storeOwnerPath);
        }
    }
}
