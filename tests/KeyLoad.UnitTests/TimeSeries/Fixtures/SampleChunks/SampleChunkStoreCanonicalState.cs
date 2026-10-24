using KeyLoad.Storage;

namespace KeyLoad.UnitTests.TimeSeries;

internal sealed record SampleChunkStoreCanonicalState(long Position, KeyValueRecord[] Records);
