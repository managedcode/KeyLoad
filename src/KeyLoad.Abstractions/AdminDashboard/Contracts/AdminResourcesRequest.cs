namespace KeyLoad;

/// <summary>Requests an administrator-authorized database catalog metadata page.</summary>
/// <param name="TenantId">Explicit persisted catalog tenant scope.</param>
/// <param name="DatabaseId">Explicit persisted catalog database scope.</param>
/// <param name="AfterName">Exclusive resource-name continuation, or null for the first page.</param>
/// <param name="Limit">Maximum returned metadata items.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.AdminResourcesRequest)]
public sealed record AdminResourcesRequest([property: Orleans.Id(0)] string TenantId, [property: Orleans.Id(1)] string DatabaseId, [property: Orleans.Id(2)] string? AfterName = null,
    [property: Orleans.Id(3)] int Limit = AdminDashboardProtocol.DefaultPageSize);
