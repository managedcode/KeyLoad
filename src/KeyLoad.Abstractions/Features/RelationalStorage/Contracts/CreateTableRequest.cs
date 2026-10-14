namespace KeyLoad;

[Orleans.GenerateSerializer]
[Orleans.Alias(CreateTableContract.Alias)]
internal sealed record CreateTableRequest(
    [property: Orleans.Id(CreateTableContract.TenantField)] string TenantId,
    [property: Orleans.Id(CreateTableContract.DatabaseField)] string DatabaseId,
    [property: Orleans.Id(CreateTableContract.DefinitionField)] ResourceDefinition Definition);
