namespace KeyLoad.UnitTests.TimeSeries;

internal sealed record SampleChunkStoreSourceCut(SampleRecord[] Source, SampleRecord[] Decoded,
    byte[] Encoded, SampleChunkStoreReaderResults Readers, SampleChunkStoreCanonicalState CanonicalState);
