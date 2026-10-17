namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class MetadataBootstrapPersistedCorruptionTests
{
    [Test]
    [Arguments("version")]
    [Arguments("empty")]
    [Arguments("oversized")]
    public Task NativeCommittedCatalogCorruptionRefusesNewBootstrapThenExactFaultUndoAndTwoColdOwnersPreserveOriginals(string variant)
        => MetadataBootstrapPersistedCorruptionFlow.RunAsync(variant, TestContext.Current!.Execution.CancellationToken);
}
