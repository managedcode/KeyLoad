namespace KeyLoad;

/// <summary>Requests creation or replacement of a subscription definition.</summary>
/// <param name="CommandId">The idempotent command identifier.</param>
/// <param name="Subscription">The subscription to configure.</param>
/// <param name="Definition">The principal, policy, and event filter definition.</param>
/// <param name="Start">The initial source position policy.</param>
/// <param name="Cursor">The position cursor when <paramref name="Start"/> is <see cref="SubscriptionStart.FromCursor"/>.</param>
/// <param name="ExpectedGeneration">The paused existing generation to replace while preserving its contiguous checkpoint; null retains creation semantics.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.ConfigureSubscriptionRequest)]
public sealed record ConfigureSubscriptionRequest([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] SubscriptionRef Subscription, [property: Orleans.Id(2)] SubscriptionDefinition Definition,
    [property: Orleans.Id(3)] SubscriptionStart Start = SubscriptionStart.FromBeginning, [property: Orleans.Id(4)] string? Cursor = null,
    [property: Orleans.Id(5), System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] long? ExpectedGeneration = null);
