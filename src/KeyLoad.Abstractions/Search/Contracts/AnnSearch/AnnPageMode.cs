namespace KeyLoad;

/// <summary>Describes the actual completed search mode without exposing maintenance corpus metadata.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(AnnSearchContractAliases.Mode)]
public enum AnnPageMode
{
    /// <summary>The eligible native set was completely ranked by actual exact work.</summary>
    Exact = 0,
    /// <summary>The native graph supplied bounded approximate candidates.</summary>
    Approximate = 1
}
