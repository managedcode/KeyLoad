namespace KeyLoad;

/// <summary>One observed physical file; never exposes an absolute path or file contents.</summary>
/// <param name="Path">Path relative to the owning physical node directory.</param>
/// <param name="Category">Canonical, replica, backup or other physical file category.</param>
/// <param name="Bytes">Observed file length, not allocated disk blocks.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.AdminFileInfo)]
public sealed record AdminFileInfo([property: Orleans.Id(0)] string Path, [property: Orleans.Id(1)] string Category, [property: Orleans.Id(2)] long Bytes);
