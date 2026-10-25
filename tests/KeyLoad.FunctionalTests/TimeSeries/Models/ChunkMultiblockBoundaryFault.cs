namespace KeyLoad.FunctionalTests.TimeSeries;

internal sealed record ChunkMultiblockBoundaryFault(byte[] BlockKey, byte[] ManifestKey,
    byte[] BlockOriginal, byte[] ManifestOriginal);
