namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed record SampleChunkStoreSourceCut(SampleRecord[] Source, SampleRecord[] Decoded,
    byte[] Encoded, SampleChunkStoreReaderResults Readers, SampleChunkStoreCanonicalState CanonicalState);
