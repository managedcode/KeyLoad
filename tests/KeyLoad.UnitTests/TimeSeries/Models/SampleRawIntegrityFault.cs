namespace KeyLoad.UnitTests.TimeSeries;

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
