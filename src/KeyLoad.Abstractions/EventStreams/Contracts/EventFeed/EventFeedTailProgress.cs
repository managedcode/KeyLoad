namespace KeyLoad;

/// <summary>One complete original persisted offer delivered by an authorized read-only tail.</summary>
/// <param name="Version">The current native progress contract version.</param>
/// <param name="OriginalOfferResult">The actual retained public offer, including its original receipt and tokens.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(EventFeedTailProgress.SerializerAlias)]
public sealed record EventFeedTailProgress(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] EventFeedControlResult OriginalOfferResult)
{
    internal const string SerializerAlias = "keyload.event-feed-tail-progress.v1";
}
