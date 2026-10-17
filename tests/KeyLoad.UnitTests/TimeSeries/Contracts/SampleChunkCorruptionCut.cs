namespace KeyLoad.UnitTests.Features.TimeSeries;

internal enum SampleChunkCorruptionCut
{
    MissingManifest,
    ManifestDigest,
    ManifestVersion,
    ManifestGeneration,
    MissingBlock,
    BlockDigest,
    MissingCorrection,
    CorrectionSequence
}
