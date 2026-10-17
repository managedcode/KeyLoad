using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed record SampleChunkStoreCanonicalState(long Position, KeyValueRecord[] Records);
