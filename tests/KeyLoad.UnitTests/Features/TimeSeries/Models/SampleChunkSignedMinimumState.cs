namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed record SampleChunkSignedMinimumState(ReadSampleChunkWindowRequest Request,
    PrincipalRecord Reader, (CommandRequest Request, CommitReceipt Receipt)[] Commands);
