namespace KeyLoad;

/// <summary>Binds a canonical entity reference to its server-admitted resource incarnation.</summary>
/// <param name="Incarnation">Identifies the admitted resource lifetime; it grants no authorization.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(EntityRefLifetimeContract.Alias)]
public sealed record EntityRefLifetimeV1(
    [property: Orleans.Id(EntityRefLifetimeContract.IncarnationField)] string Incarnation);
