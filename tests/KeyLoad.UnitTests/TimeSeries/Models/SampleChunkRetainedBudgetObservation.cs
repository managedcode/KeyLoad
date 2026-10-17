namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed record SampleChunkRetainedBudgetObservation(SampleChunkWindowResult Result, long Bytes, int Records);
