namespace KeyLoad.UnitTests.TimeSeries;

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
