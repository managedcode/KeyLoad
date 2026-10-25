namespace KeyLoad.FunctionalTests.Search;

internal sealed class AnnLifetimeAuthorityTests
{
    [Test]
    public Task IndependentStoresIntersectBothRowOrPoliciesAndOwnFieldSchemasBeforeExactCandidates()
        => AnnLifetimeFixture.RunAsync(async (source, target, runtime, pin, receipt) =>
        {
            await Assert.That(source.Store.Identity.Incarnation).IsNotEqualTo(target.Store.Identity.Incarnation);
            var metadata = AnnLifetimeRead.Capture(source);
            await Assert.That(metadata.Principal.PolicyEpoch).IsEqualTo(AnnLifetimeProtocol.SourceEpoch);
            await Assert.That(metadata.Definition.SchemaVersion).IsEqualTo(AnnLifetimeProtocol.SourceSchema);
            var sourceRows = AnnMaintenanceTestData.Snapshot(source);
            var targetRows = AnnMaintenanceTestData.Snapshot(target);
            var sourceCut = source.Store.Position;
            var targetCut = target.Store.Position;
            await AnnLifetimeAssertions.PageAsync(await AnnLifetimeRead.SearchAsync(source, target, runtime,
                AnnLifetimeRead.Request(target, pin)), target, targetCut);
            await AnnLifetimeRead.WaitAsync(source, target, runtime, pin, receipt);
            await AnnLifetimeAssertions.UnchangedAsync(source, sourceRows, sourceCut, target, targetRows, targetCut);
            AnnLifetimeSeed.Principal(source, true, AnnLifetimeProtocol.SourceEpoch + AnnLifetimeProtocol.NextEpoch,
                administrator: true);
            var request = AnnLifetimeRead.Request(target, pin);
            request = request with
            {
                Search = request.Search with
                {
                    AllowedIds = [AnnSeedTestSupport.Id(AnnLifetimeProtocol.BothOwner)]
                }
            };
            await AnnLifetimeAssertions.PageAsync(await AnnLifetimeRead.SearchAsync(source, target, runtime, request),
                target, target.Store.Position, administrator: true);
        });

    [Test]
    public Task SourceSchemaPrecedesBothRawUseDenialsAndIndependentRepairReturnsSameHealthyPage()
        => AnnLifetimeFixture.RunAsync(async (source, target, runtime, pin, receipt) =>
        {
            var epoch = AnnLifetimeProtocol.SourceEpoch + AnnLifetimeProtocol.NextEpoch;
            AnnLifetimeSeed.Principal(source, true, epoch, schema: false, use: false);
            AnnLifetimeSeed.Principal(target, false, AnnLifetimeProtocol.TargetEpoch + AnnLifetimeProtocol.NextEpoch, use: false);
            var absentIndex = AnnLifetimeRead.Request(target, pin) with
            {
                IndexGeneration = AnnLifetimeProtocol.ForeignGeneration
            };
            await AnnLifetimeAssertions.RejectedAsync(() => AnnLifetimeRead.SearchAsync(source, target, runtime, absentIndex), ErrorCode.Validation);
            AnnLifetimeSeed.Principal(source, true, ++epoch, use: false);
            await AnnLifetimeAssertions.RejectedAsync(() => AnnLifetimeRead.SearchAsync(source, target, runtime, absentIndex), ErrorCode.PermissionDenied);
            AnnLifetimeSeed.Principal(source, true, ++epoch);
            AnnLifetimeSeed.Principal(target, false,
                AnnLifetimeProtocol.TargetEpoch + AnnLifetimeProtocol.NextEpoch + AnnLifetimeProtocol.NextEpoch);
            await AnnLifetimeAssertions.PageAsync(await AnnLifetimeRead.SearchAsync(source, target, runtime,
                AnnLifetimeRead.Request(target, pin)), target, target.Store.Position);
        });

    [Test]
    public Task ActualTargetLifetimeWrongResourceAbsenceAndCorruptBytesRefuseBeforeIndexThenRestoreHealthyPage()
        => AnnLifetimeFixture.RunAsync((source, target, runtime, pin, receipt)
            => AnnLifetimeFaults.TargetLifetimeAsync(source, target, runtime, pin));
}
