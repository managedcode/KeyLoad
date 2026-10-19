namespace KeyLoad.Clustering;

/// <summary>Carries only the original caller's remaining execution read ceilings and absolute deadline.</summary>
[GenerateSerializer, Alias(BackgroundReadBudget.SerializerAlias)]
public sealed record BackgroundReadBudget([property: Id(0)] DateTimeOffset ExpiresAt,
    [property: Id(1)] long MaximumReadBytes, [property: Id(2)] int MaximumRecords,
    [property: Id(3)] int MaximumResultBytes)
{
    internal const string SerializerAlias = "keyload.orleans.background-read-budget";
}
