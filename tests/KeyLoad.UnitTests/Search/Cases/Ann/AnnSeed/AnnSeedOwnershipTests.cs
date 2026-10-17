using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class AnnSeedOwnershipTests
{
    [Test]
    public async Task HistoricalSeedOwnsVectorAndScopeAfterLaterCanonicalWritesAndPolicyChanges()
    {
        using var database = AnnSeedTestSupport.Create(1);
        var seed = AnnSeedTestSupport.Capture(database);
        var originalId = seed.Records.Single().DocumentId;
        var originalValues = seed.Records.Single().Values.ToArray();
        var originalCut = seed.Cut;
        var originalHash = seed.CorpusSha256;
        var sourceSpace = AnnSeedTestSupport.Space();
        var capturedSpace = seed.Scope.Space;

        AnnSeedTestSupport.CommitVector(database, AnnSeedTestSupport.Id(0),
            ImmutableArray.Create(7f, 8f, 9f), sourceSpace);
        var principal = database.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(
            AnnSeedTestSupport.Principal)))!;
        AnnSeedTestSupport.Persist(database, principal.Id, Capability.VectorSearch, [], owner: principal.OwnerId,
            restrictRows: principal.RestrictRows, policyEpoch: principal.PolicyEpoch + 1);
        var refreshed = AnnSeedTestSupport.CaptureFailure(database, AnnSeedTestSupport.Principal);

        await Assert.That(refreshed.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(seed.Records.Single().DocumentId).IsEqualTo(originalId);
        await Assert.That(seed.Records.Single().Values.ToArray()).IsEquivalentTo(originalValues, CollectionOrdering.Matching);
        await Assert.That(seed.Cut).IsEqualTo(originalCut);
        await Assert.That(seed.CorpusSha256).IsEqualTo(originalHash);
        await Assert.That(seed.Scope.Space).IsEqualTo(capturedSpace);
        await Assert.That(seed.Scope.Space).IsEqualTo(sourceSpace);
    }

    [Test]
    public async Task SeedSurfaceContainsOnlyOwnedScopeCutAndVectorValues()
    {
        using var database = AnnSeedTestSupport.Create(1);
        var seed = AnnSeedTestSupport.Capture(database);
        var scopeProperties = typeof(AnnSeedScope).GetProperties().Select(property => property.Name).ToArray();
        var cutProperties = typeof(AnnSeedCut).GetProperties().Select(property => property.Name).ToArray();
        var seedProperties = typeof(AnnSeed).GetProperties().Select(property => property.Name).ToArray();

        await Assert.That(scopeProperties).IsEquivalentTo(
            ["PrincipalId", "PolicyEpoch", "Partition", "Collection", "Field", "SchemaVersion", "Space", "EvaluatedAt"],
            CollectionOrdering.Matching);
        await Assert.That(cutProperties).IsEquivalentTo(
            ["NodeId", "Incarnation", "StoreFormatVersion", "KeyCodecVersion", "ReadGeneration", "Position",
             "AppliedPosition", "OutboxTail", "OutboxFirstAvailable"], CollectionOrdering.Matching);
        await Assert.That(seedProperties).IsEquivalentTo(
            ["Scope", "Cut", "Records", "CorpusSha256", "OwnedBytesUpperBound", "PeakBytesUpperBound", "ReadBytes", "WorkUnits"],
            CollectionOrdering.Matching);
        await Assert.That(seed.Scope.PrincipalId).IsEqualTo(AnnSeedTestSupport.Principal);
        await Assert.That(seed.Records).HasSingleItem();
    }
}
