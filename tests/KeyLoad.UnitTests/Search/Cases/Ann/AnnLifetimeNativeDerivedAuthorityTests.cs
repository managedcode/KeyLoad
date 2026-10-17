namespace KeyLoad.UnitTests.Features.Search;

internal sealed class AnnLifetimeNativeDerivedAuthorityTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task GenuineSameAndCrossCollectionDerivedVectorsRequireBothOwnInputSchemaAndRawUseThenIndependentRepair(bool cross)
        => AnnLifetimeNativeFixture.RunAsync(async (source, target, runtime, pin, receipt) =>
        {
            var request = AnnLifetimeNativeRead.Request(target, pin);
            var sourceRows = NativeAnnMaintenanceTestData.Snapshot(source);
            var targetRows = NativeAnnMaintenanceTestData.Snapshot(target);
            var sourceCut = source.Store.Position;
            var targetCut = target.Store.Position;
            await AnnLifetimeNativeDerivedAssertions.PageAsync(await AnnLifetimeNativeRead.SearchAsync(source, target, runtime, request),
                target, targetCut, cross, allowed: false, sourceInputSchema: false);
            await AnnLifetimeNativeAssertions.UnchangedAsync(source, sourceRows, sourceCut, target, targetRows, targetCut);
            AnnLifetimeNativeDerived.Repair(source, source: true, rawUse: false);
            await AnnLifetimeNativeDerivedAssertions.PageAsync(await AnnLifetimeNativeRead.SearchAsync(source, target, runtime, request),
                target, target.Store.Position, cross, allowed: false);
            AnnLifetimeNativeDerived.Repair(source, source: true, rawUse: true);
            await AnnLifetimeNativeDerivedAssertions.PageAsync(await AnnLifetimeNativeRead.SearchAsync(source, target, runtime, request),
                target, target.Store.Position, cross, allowed: true);
            AnnLifetimeNativeDerived.Repair(target, source: false, rawUse: false);
            await AnnLifetimeNativeDerivedAssertions.PageAsync(await AnnLifetimeNativeRead.SearchAsync(source, target, runtime, request),
                target, target.Store.Position, cross, allowed: false);
            AnnLifetimeNativeDerived.Repair(target, source: false, rawUse: true);
            await AnnLifetimeNativeDerivedAssertions.PageAsync(await AnnLifetimeNativeRead.SearchAsync(source, target, runtime, request),
                target, target.Store.Position, cross, allowed: true);
        }, derived: true, cross: cross);

    [Test]
    public Task GenuineCrossCollectionDerivedInputTargetLifetimeWrongResourceAbsenceAndCorruptBytesRefuseThenRepair()
        => AnnLifetimeNativeFixture.RunAsync(async (source, target, runtime, pin, receipt) =>
        {
            AnnLifetimeNativeDerived.Repair(source, source: true, rawUse: true);
            await AnnLifetimeNativeFaults.TargetLifetimeAsync(source, target, runtime, pin, AnnLifetimeNativeProtocol.InputCollection,
                (page, cut) => AnnLifetimeNativeDerivedAssertions.PageAsync(page, target, cut, cross: true, allowed: true));
        }, derived: true, cross: true);
}
