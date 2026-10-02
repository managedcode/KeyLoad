using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Carries a time-series sample and its event identity.</summary>
/// <param name="EventId">Identifies the event.</param>
/// <param name="Timestamp">Specifies the timestamp value.</param>
/// <param name="Value">Specifies the value value.</param>
public sealed record SampleData(string EventId, DateTimeOffset Timestamp, double Value);

/// <summary>Appends samples and tags to a time-series resource.</summary>
/// <param name="SeriesSet">Identifies the time-series set.</param>
/// <param name="SeriesId">Identifies the time series.</param>
/// <param name="Samples">Lists samples to append.</param>
/// <param name="TagsJson">Contains the sample tags as JSON.</param>
public sealed record AppendSamples(string SeriesSet, string SeriesId, ImmutableArray<SampleData> Samples, string TagsJson = "{}") : Mutation(SeriesSet);

/// <summary>Represents a stored time-series sample and its sequence and tags.</summary>
/// <param name="SeriesId">Identifies the time series.</param>
/// <param name="Sample">Specifies the sample value.</param>
/// <param name="Sequence">Specifies the sequence value.</param>
/// <param name="TagsJson">Contains the sample tags as JSON.</param>
public sealed record SampleRecord(string SeriesId, SampleData Sample, long Sequence, string TagsJson);
