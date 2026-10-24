namespace KeyLoad.Core;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.InternalSerialization.CoreAliases.SubscriptionCompletion)]
internal sealed record SubscriptionCompletion(
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.SubscriptionCompletionFields.Generation)] long Generation,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.SubscriptionCompletionFields.Position)] long Position,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.SubscriptionCompletionFields.Outcome)] string Outcome,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.SubscriptionCompletionFields.PolicyEpoch)] long PolicyEpoch);
