namespace KeyLoad.FunctionalTests.TimeSeries;

internal enum SampleChunkCorruptionCut
{
    MissingManifest,
    ManifestDigest,
    ManifestGeneration,
    MissingBlock,
    BlockDigest,
    MissingCorrection,
    CorrectionSequence
}
