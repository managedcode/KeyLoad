namespace KeyLoad.FunctionalTests.Search;

internal sealed class ProjectionDigestWholeFlowTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task RealVectorOrEffectDigestCorruptionRefusesSearchAndAnnThenRepairReplayReclassificationAndColdRemainHealthy(bool effect)
        => ProjectionDigestWholeFlow.RunAsync(effect);
}
