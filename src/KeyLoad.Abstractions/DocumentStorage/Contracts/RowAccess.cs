namespace KeyLoad;

/// <summary>Describes row ownership and project metadata used by row-level access policy.</summary>
/// <param name="OwnerId">Identifies the principal owner.</param>
/// <param name="ProjectId">Identifies the optional project.</param>
/// <param name="TeamId">Optional tenant-scoped team identity requiring persisted membership.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.RowAccess)]
public sealed record RowAccess([property: Orleans.Id(0)] string? OwnerId = null, [property: Orleans.Id(1)] string? ProjectId = null,
    [property: Orleans.Id(2)] string? TeamId = null);
