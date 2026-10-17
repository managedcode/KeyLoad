namespace KeyLoad.Core;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.SubscriptionCompletion)]
internal sealed record SubscriptionCompletion(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.SubscriptionCompletionFields.Generation)] long Generation,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.SubscriptionCompletionFields.Position)] long Position,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.SubscriptionCompletionFields.Outcome)] string Outcome,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.SubscriptionCompletionFields.PolicyEpoch)] long PolicyEpoch);
