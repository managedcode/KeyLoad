namespace KeyLoad.FunctionalTests.Search;

internal sealed class AnnLifetimeDerivedAuthorityTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task GenuineSameAndCrossCollectionDerivedVectorsRequireBothOwnInputSchemaAndRawUseThenIndependentRepair(bool cross)
        => AnnLifetimeFixture.RunAsync(async (source, target, runtime, pin, receipt) =>
        {
            var request = AnnLifetimeRead.Request(target, pin);
            var sourceRows = AnnMaintenanceTestData.Snapshot(source);
            var targetRows = AnnMaintenanceTestData.Snapshot(target);
            var sourceCut = source.Store.Position;
            var targetCut = target.Store.Position;
            await AnnLifetimeDerivedAssertions.PageAsync(await AnnLifetimeRead.SearchAsync(source, target, runtime, request),
                target, targetCut, cross, allowed: false, sourceInputSchema: false);
            await AnnLifetimeAssertions.UnchangedAsync(source, sourceRows, sourceCut, target, targetRows, targetCut);
            AnnLifetimeDerived.Repair(source, source: true, rawUse: false);
            await AnnLifetimeDerivedAssertions.PageAsync(await AnnLifetimeRead.SearchAsync(source, target, runtime, request),
                target, target.Store.Position, cross, allowed: false);
            AnnLifetimeDerived.Repair(source, source: true, rawUse: true);
            await AnnLifetimeDerivedAssertions.PageAsync(await AnnLifetimeRead.SearchAsync(source, target, runtime, request),
                target, target.Store.Position, cross, allowed: true);
            AnnLifetimeDerived.Repair(target, source: false, rawUse: false);
            await AnnLifetimeDerivedAssertions.PageAsync(await AnnLifetimeRead.SearchAsync(source, target, runtime, request),
                target, target.Store.Position, cross, allowed: false);
            AnnLifetimeDerived.Repair(target, source: false, rawUse: true);
            await AnnLifetimeDerivedAssertions.PageAsync(await AnnLifetimeRead.SearchAsync(source, target, runtime, request),
                target, target.Store.Position, cross, allowed: true);
        }, derived: true, cross: cross);

    [Test]
    public Task GenuineCrossCollectionDerivedInputTargetLifetimeWrongResourceAbsenceAndCorruptBytesRefuseThenRepair()
        => AnnLifetimeFixture.RunAsync(async (source, target, runtime, pin, receipt) =>
        {
            AnnLifetimeDerived.Repair(source, source: true, rawUse: true);
            await AnnLifetimeFaults.TargetLifetimeAsync(source, target, runtime, pin, AnnLifetimeProtocol.InputCollection,
                (page, cut) => AnnLifetimeDerivedAssertions.PageAsync(page, target, cut, cross: true, allowed: true));
        }, derived: true, cross: true);
}
