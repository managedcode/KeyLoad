namespace KeyLoad.Orleans;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(EventFeedWakeupHint.SerializerAlias)]
internal sealed record EventFeedWakeupHint(
    [property: global::Orleans.Id(EventFeedWakeupHint.VersionField)] int Version,
    [property: global::Orleans.Id(EventFeedWakeupHint.MapField)] Guid MapId,
    [property: global::Orleans.Id(EventFeedWakeupHint.RevisionField)] long MapRevision,
    [property: global::Orleans.Id(EventFeedWakeupHint.CoverageField)] long CoverageGeneration)
{
    internal const string SerializerAlias = "keyload.orleans.event-feed-wakeup-hint.v1";
    internal const int VersionField = 0;
    internal const int MapField = 1;
    internal const int RevisionField = 2;
    internal const int CoverageField = 3;
}
