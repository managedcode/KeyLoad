namespace KeyLoad.FunctionalTests.TimeSeries;

internal sealed record SampleChunkRetainedBudgetObservation(SampleChunkWindowResult Result, long Bytes, int Records);
