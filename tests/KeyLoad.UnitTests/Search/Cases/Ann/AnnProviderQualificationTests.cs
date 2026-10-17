using static KeyLoad.UnitTests.Features.Search.AnnProviderQualificationProtocol;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class AnnProviderQualificationTests
{
    [Test]
    [Arguments(DistanceMetric.Cosine, AnnProviderQualificationDamage.HeaderVersion)]
    [Arguments(DistanceMetric.Cosine, AnnProviderQualificationDamage.HeaderEntryPoint)]
    [Arguments(DistanceMetric.Cosine, AnnProviderQualificationDamage.HeaderRetainedBytes)]
    [Arguments(DistanceMetric.Cosine, AnnProviderQualificationDamage.NodeOrdinal)]
    [Arguments(DistanceMetric.Cosine, AnnProviderQualificationDamage.NodeRevision)]
    [Arguments(DistanceMetric.Cosine, AnnProviderQualificationDamage.NonfiniteVector)]
    [Arguments(DistanceMetric.Cosine, AnnProviderQualificationDamage.SelfNeighbor)]
    [Arguments(DistanceMetric.Cosine, AnnProviderQualificationDamage.DuplicateNeighbor)]
    [Arguments(DistanceMetric.Euclidean, AnnProviderQualificationDamage.HeaderVersion)]
    [Arguments(DistanceMetric.Euclidean, AnnProviderQualificationDamage.HeaderEntryPoint)]
    [Arguments(DistanceMetric.Euclidean, AnnProviderQualificationDamage.HeaderRetainedBytes)]
    [Arguments(DistanceMetric.Euclidean, AnnProviderQualificationDamage.NodeOrdinal)]
    [Arguments(DistanceMetric.Euclidean, AnnProviderQualificationDamage.NodeRevision)]
    [Arguments(DistanceMetric.Euclidean, AnnProviderQualificationDamage.NonfiniteVector)]
    [Arguments(DistanceMetric.Euclidean, AnnProviderQualificationDamage.SelfNeighbor)]
    [Arguments(DistanceMetric.Euclidean, AnnProviderQualificationDamage.DuplicateNeighbor)]
    [Arguments(DistanceMetric.DotProduct, AnnProviderQualificationDamage.HeaderVersion)]
    [Arguments(DistanceMetric.DotProduct, AnnProviderQualificationDamage.HeaderEntryPoint)]
    [Arguments(DistanceMetric.DotProduct, AnnProviderQualificationDamage.HeaderRetainedBytes)]
    [Arguments(DistanceMetric.DotProduct, AnnProviderQualificationDamage.NodeOrdinal)]
    [Arguments(DistanceMetric.DotProduct, AnnProviderQualificationDamage.NodeRevision)]
    [Arguments(DistanceMetric.DotProduct, AnnProviderQualificationDamage.NonfiniteVector)]
    [Arguments(DistanceMetric.DotProduct, AnnProviderQualificationDamage.SelfNeighbor)]
    [Arguments(DistanceMetric.DotProduct, AnnProviderQualificationDamage.DuplicateNeighbor)]
    public async Task AcAnnProviderQual001To004ChecksumValidSemanticRefusalRepairsOriginalHistoryAndColdRebuild(
        DistanceMetric metric, AnnProviderQualificationDamage damage)
    {
        var context = TestContext.Current ?? throw new InvalidOperationException(MissingContext);
        await AnnProviderQualificationFlow.RunAsync(metric, damage, context.Execution.CancellationToken);
    }
}
