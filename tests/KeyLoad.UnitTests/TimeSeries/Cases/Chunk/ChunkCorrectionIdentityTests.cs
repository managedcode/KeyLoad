namespace KeyLoad.UnitTests.TimeSeries;

internal sealed class ChunkCorrectionIdentityTests
{
    [Test]
    public Task AcChunk008010011012SelectedCorrectionCannotDuplicateSealedBaseEventIdentity()
        => ChunkCorrectionIdentityTrial.Run();
}
