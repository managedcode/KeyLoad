namespace KeyLoad;

/// <summary>Requests removal of the table resolved once by the authenticated database connection.</summary>
/// <param name="TenantId">The tenant scope.</param>
/// <param name="DatabaseId">The database scope.</param>
/// <param name="ResourceName">The table name resolved by the server.</param>
[Orleans.GenerateSerializer, Orleans.Alias(DropTableContract.DropTableRequestAlias)]
public sealed record DropTableRequest(
    [property: Orleans.Id(0)] string TenantId,
    [property: Orleans.Id(1)] string DatabaseId,
    [property: Orleans.Id(2)] string ResourceName);
