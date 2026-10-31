namespace KeyLoad.Clustering;

/// <summary>Carries the original caller's absolute expiry to an owner read.</summary>
[GenerateSerializer, Alias(BackgroundReadBudget.SerializerAlias)]
public sealed record BackgroundReadBudget([property: Id(0)] DateTimeOffset ExpiresAt)
{
    internal const string SerializerAlias = "keyload.orleans.background-read-budget";
}
