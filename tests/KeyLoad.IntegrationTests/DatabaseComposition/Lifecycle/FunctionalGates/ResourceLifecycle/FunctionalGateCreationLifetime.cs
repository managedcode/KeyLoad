using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.Server;
using KeyLoad.Storage;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

/// <summary>Independent creation-identity oracle over the original caller-owned creation, never returned document values.</summary>
internal static class FunctionalGateCreationLifetime
{
    private const string IncarnationDomain = "keyload:resource-incarnation";
    private const string IdProperty = "id";
    private const string KindProperty = "kind";
    private const string PayloadProperty = "payloadJson";
    private const string PrincipalProperty = "principalId";

    internal static void Expect(FunctionalGateContext context, Guid commandId, ResourceDefinition definition, bool table)
        => context.ReferenceBindings.ExpectCreation(context.Partition, definition.Name,
            Create(context, commandId, definition, table));

    internal static EntityRefLifetime Create(FunctionalGateContext context, Guid commandId,
        ResourceDefinition definition, bool table)
    {
        if (commandId == Guid.Empty)
        {
            throw new InvalidOperationException("The original public creation command identity is required.");
        }
        var partition = context.Partition;
        var payload = table
            ? JsonSerializer.Serialize(new CreateTableRequest(partition.TenantId, partition.DatabaseId, definition), JsonDefaults.Options)
            : JsonSerializer.Serialize(new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition), JsonDefaults.Options);
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes))
        {
            writer.WriteStartObject();
            writer.WriteString(IdProperty, commandId);
            writer.WritePropertyName(KindProperty);
            JsonSerializer.Serialize(writer, table ? OperationKind.CreateTable : OperationKind.ConfigureResource, JsonDefaults.Options);
            writer.WriteString(PayloadProperty, payload);
            writer.WriteString(PrincipalProperty, PartitionStoreProtocol.AdministratorId);
            writer.WriteEndObject();
        }
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray()));
        var incarnation = SHA256.HashData(KeyCodec.Encode(IncarnationDomain, partition.TenantId,
            partition.DatabaseId, definition.Name, fingerprint));
        return new(Convert.ToHexStringLower(incarnation));
    }
}
