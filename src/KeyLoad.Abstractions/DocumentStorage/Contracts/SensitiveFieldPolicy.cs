using System.Text.Json.Serialization;

namespace KeyLoad;

/// <summary>Defines classification and grants for a sensitive field or header.</summary>
/// <param name="Path">Identifies a field path.</param>
/// <param name="Classification">Names the data classification.</param>
/// <param name="RawReadGrant">Names the grant required to read raw values.</param>
/// <param name="RawUseGrant">Names the grant required to use raw values.</param>
/// <param name="WriteGrant">Names the grant required to write values.</param>
/// <param name="RequiredForProcessing">Requires this field for processing when true.</param>
/// <param name="SchemaReadGrant">Names the grant required to disclose field schema, or null for public schema.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.SensitiveFieldPolicy)]
public sealed record SensitiveFieldPolicy([property: Orleans.Id(0)] string Path, [property: Orleans.Id(1)] string Classification, [property: Orleans.Id(2)] string RawReadGrant = SensitiveFieldPolicy.DefaultRawReadGrant,
    [property: Orleans.Id(3)] string RawUseGrant = SensitiveFieldPolicy.DefaultRawUseGrant, [property: Orleans.Id(4)] string WriteGrant = SensitiveFieldPolicy.DefaultWriteGrant, [property: Orleans.Id(5)] bool RequiredForProcessing = false,
    [property: Orleans.Id(6), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SchemaReadGrant = null)
{
    private const string DefaultRawReadGrant = "pii.read";
    private const string DefaultRawUseGrant = "pii.use";
    private const string DefaultWriteGrant = "pii.write";
}
