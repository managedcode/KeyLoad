namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed record SampleChunkStoreReaderResults(SampleRecord[] Range, SampleRecord? Latest,
    SampleAggregate Aggregate, SampleAggregateWindow[] Windows);
