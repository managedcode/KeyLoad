using KeyLoad.Storage;

namespace KeyLoad.FunctionalTests.TimeSeries;

internal sealed record SampleChunkStoreCanonicalState(long Position, KeyValueRecord[] Records);
