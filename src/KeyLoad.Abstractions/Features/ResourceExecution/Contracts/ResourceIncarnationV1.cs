namespace KeyLoad;

/// <summary>Identifies one persisted resource creation independently of its policy schema version.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(ResourceLifetimeContract.IncarnationAlias)]
internal sealed record ResourceIncarnationV1([property: Orleans.Id(ResourceLifetimeContract.IncarnationValueField)] string Value);
