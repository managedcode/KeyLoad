namespace KeyLoad.UnitTests.Features.TimeSeries;

internal enum SampleRawIntegrityFault
{
    Series,
    Timestamp,
    Sequence,
    NonpositiveSequence,
    NonfiniteValue,
    MissingSample,
    RuntimeWrongRoot,
    RuntimeMalformed
}
