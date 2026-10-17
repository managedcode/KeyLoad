using System.Text.Json.Serialization;

namespace KeyLoad;

/// <summary>Requests configuration of a resource for a tenant and database.</summary>
/// <param name="TenantId">Identifies the owning tenant.</param>
/// <param name="DatabaseId">Identifies the database.</param>
/// <param name="Definition">Provides the resource configuration.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ConfigureResourceRequest)]
public sealed record ConfigureResourceRequest([property: Orleans.Id(0)] string TenantId, [property: Orleans.Id(1)] string DatabaseId, [property: Orleans.Id(2)] ResourceDefinition Definition)
{
    /// <summary>Gets the existing resource version required for a policy-only replacement.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Orleans.Id(3)]
    public long? ExpectedSchemaVersion
    {
        get; init;
    }
}
