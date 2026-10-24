namespace KeyLoad.UnitTests.TimeSeries;

internal sealed record SampleChunkStoreReaderResults(SampleRecord[] Range, SampleRecord? Latest,
    SampleAggregate Aggregate, SampleAggregateWindow[] Windows);
