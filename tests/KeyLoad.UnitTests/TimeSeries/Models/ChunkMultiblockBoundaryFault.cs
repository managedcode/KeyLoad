namespace KeyLoad.UnitTests.TimeSeries;

internal sealed record ChunkMultiblockBoundaryFault(byte[] BlockKey, byte[] ManifestKey,
    byte[] BlockOriginal, byte[] ManifestOriginal);
