namespace KeyLoad;

/// <summary>Controls which normalized query terms a document must contain in the text branch.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.TextMatchMode)]
public enum TextMatchMode
{
    /// <summary>At least one distinct query term must match.</summary>
    Any = 0,
    /// <summary>Every distinct query term must match before ranking and result limits.</summary>
    All = 1
}
