namespace KeyLoad.UnitTests.Features.Search;

internal sealed class AnnLifetimeNativeAuthorityTests
{
    [Test]
    public Task IndependentNativeStoresIntersectBothRowOrPoliciesAndOwnFieldSchemasBeforeNativeExactCandidates()
        => AnnLifetimeNativeFixture.RunAsync(async (source, target, runtime, pin, receipt) =>
        {
            await Assert.That(source.Store.Identity.Incarnation).IsNotEqualTo(target.Store.Identity.Incarnation);
            var metadata = AnnLifetimeNativeRead.Capture(source);
            await Assert.That(metadata.Principal.PolicyEpoch).IsEqualTo(AnnLifetimeNativeProtocol.SourceEpoch);
            await Assert.That(metadata.Definition.SchemaVersion).IsEqualTo(AnnLifetimeNativeProtocol.SourceSchema);
            var sourceRows = NativeAnnMaintenanceTestData.Snapshot(source);
            var targetRows = NativeAnnMaintenanceTestData.Snapshot(target);
            var sourceCut = source.Store.Position;
            var targetCut = target.Store.Position;
            await AnnLifetimeNativeAssertions.PageAsync(await AnnLifetimeNativeRead.SearchAsync(source, target, runtime,
                AnnLifetimeNativeRead.Request(target, pin)), target, targetCut);
            await AnnLifetimeNativeRead.WaitAsync(source, target, runtime, pin, receipt);
            await AnnLifetimeNativeAssertions.UnchangedAsync(source, sourceRows, sourceCut, target, targetRows, targetCut);
            AnnLifetimeNativeSeed.Principal(source, true, AnnLifetimeNativeProtocol.SourceEpoch + AnnLifetimeNativeProtocol.NextEpoch,
                administrator: true);
            var request = AnnLifetimeNativeRead.Request(target, pin);
            request = request with
            {
                Search = request.Search with
                {
                    AllowedIds = [AnnSeedTestSupport.Id(AnnLifetimeNativeProtocol.BothOwner)]
                }
            };
            await AnnLifetimeNativeAssertions.PageAsync(await AnnLifetimeNativeRead.SearchAsync(source, target, runtime, request),
                target, target.Store.Position, administrator: true);
        });

    [Test]
    public Task SourceSchemaPrecedesBothRawUseDenialsAndIndependentNativeRepairReturnsSameHealthyPage()
        => AnnLifetimeNativeFixture.RunAsync(async (source, target, runtime, pin, receipt) =>
        {
            var epoch = AnnLifetimeNativeProtocol.SourceEpoch + AnnLifetimeNativeProtocol.NextEpoch;
            AnnLifetimeNativeSeed.Principal(source, true, epoch, schema: false, use: false);
            AnnLifetimeNativeSeed.Principal(target, false, AnnLifetimeNativeProtocol.TargetEpoch + AnnLifetimeNativeProtocol.NextEpoch, use: false);
            var absentIndex = AnnLifetimeNativeRead.Request(target, pin) with
            {
                IndexGeneration = AnnLifetimeNativeProtocol.ForeignGeneration
            };
            await AnnLifetimeNativeAssertions.RejectedAsync(() => AnnLifetimeNativeRead.SearchAsync(source, target, runtime, absentIndex), ErrorCode.Validation);
            AnnLifetimeNativeSeed.Principal(source, true, ++epoch, use: false);
            await AnnLifetimeNativeAssertions.RejectedAsync(() => AnnLifetimeNativeRead.SearchAsync(source, target, runtime, absentIndex), ErrorCode.PermissionDenied);
            AnnLifetimeNativeSeed.Principal(source, true, ++epoch);
            AnnLifetimeNativeSeed.Principal(target, false,
                AnnLifetimeNativeProtocol.TargetEpoch + AnnLifetimeNativeProtocol.NextEpoch + AnnLifetimeNativeProtocol.NextEpoch);
            await AnnLifetimeNativeAssertions.PageAsync(await AnnLifetimeNativeRead.SearchAsync(source, target, runtime,
                AnnLifetimeNativeRead.Request(target, pin)), target, target.Store.Position);
        });

    [Test]
    public Task ActualTargetLifetimeWrongResourceAbsenceAndCorruptBytesRefuseBeforeNativeIndexThenRestoreHealthyPage()
        => AnnLifetimeNativeFixture.RunAsync((source, target, runtime, pin, receipt)
            => AnnLifetimeNativeFaults.TargetLifetimeAsync(source, target, runtime, pin));
}
