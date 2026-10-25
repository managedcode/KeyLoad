namespace KeyLoad.FunctionalTests.TimeSeries;

internal sealed class ChunkSelectedIntegrityTests
{
    [Test]
    [Arguments(ChunkSelectedIntegrityFault.WindowIdentity)]
    [Arguments(ChunkSelectedIntegrityFault.WindowStart)]
    [Arguments(ChunkSelectedIntegrityFault.SourceSequence)]
    public async Task AcChunk010012SelectedManifestContradictionRefusesColdThenExactRepairReplayAndHealthyGenerationPersist(ChunkSelectedIntegrityFault fault)
        => await ChunkSelectedIntegrityTrial.Run(fault);
}
