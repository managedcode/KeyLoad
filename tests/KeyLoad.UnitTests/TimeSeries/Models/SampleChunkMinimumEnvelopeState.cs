namespace KeyLoad.UnitTests.TimeSeries;

internal sealed record SampleChunkMinimumEnvelopeState(ReadSampleChunkWindowRequest Request,
    PrincipalRecord Reader, (CommandRequest Request, CommitReceipt Receipt)[] Commands);
