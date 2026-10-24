namespace KeyLoad.UnitTests.TimeSeries;

internal sealed record SampleChunkRetainedBudgetObservation(SampleChunkWindowResult Result, long Bytes, int Records);
