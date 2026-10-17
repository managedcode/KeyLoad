namespace KeyLoad.Core.Features.AnalyticsStore;

[Orleans.GenerateSerializer]
[Orleans.Alias(AnalyticsStoredEvent.Alias)]
internal sealed record AnalyticsStoredEvent(
    [property: Orleans.Id(0)] AnalyticsEventRecord Event,
    [property: Orleans.Id(1)] bool Deleted,
    [property: Orleans.Id(2)] DateTimeOffset UpdatedAt)
{
    internal const string Alias = "keyload.analytics-store.stored-event.v1";
}
