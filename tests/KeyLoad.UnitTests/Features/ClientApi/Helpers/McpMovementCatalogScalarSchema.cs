using System.Text.Json;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>Independent scalar, voter-array and enum branches of the complete movement schema graph.</summary>
internal static class McpMovementCatalogScalarSchema
{
    private const string PhysicalId = "physicalShardId";
    private const string Incarnation = "incarnation";
    private const string Voters = "voterIds";
    private const string Epoch = "placementEpoch";
    private const string Position = "position";
    private const string Ownership = "ownershipEpoch";
    private const string Version = "version";
    private const string Revision = "revision";

    internal static async Task RequireRequestAsync(JsonElement root, JsonElement body)
    {
        await RequireStringsAsync(root, body, [McpMovementCatalogProtocol.MoveId, McpMovementCatalogProtocol.Destination]);
        await LeafAsync(root, body, McpMovementCatalogProtocol.Revision, McpSchemaInspector.Integer);
        await EnumAsync(root, body, McpMovementCatalogProtocol.Mode, McpMovementCatalogProtocol.Modes, input: true);
    }

    internal static async Task RequireResultAsync(JsonElement root, JsonElement body)
    {
        await RequireStringsAsync(root, body, [McpMovementCatalogProtocol.MoveId]);
        await LeafAsync(root, body, McpMovementCatalogProtocol.Cut, McpSchemaInspector.Integer);
        await EnumAsync(root, body, McpMovementCatalogProtocol.Phase, McpMovementCatalogProtocol.Phases, input: false);
    }

    internal static async Task RequireStringsAsync(JsonElement root, JsonElement body, string[] fields)
    {
        foreach (var field in fields)
        { await LeafAsync(root, body, field, McpSchemaInspector.String); }
    }

    internal static async Task RequireOwnerAsync(JsonElement root, JsonElement body)
    {
        await RequireStringsAsync(root, body, [PhysicalId, Incarnation]);
        await LeafAsync(root, body, Epoch, McpSchemaInspector.Integer);
        var voters = Resolve(root, body.GetProperty(McpMovementCatalogProtocol.Properties).GetProperty(Voters));
        await Assert.That(McpSchemaInspector.HasType(voters, McpSchemaInspector.Array)).IsTrue();
        await Assert.That(McpSchemaInspector.HasType(voters, McpSchemaInspector.Null)).IsFalse();
        var item = Resolve(root, voters.GetProperty(McpSchemaInspector.Items));
        await Assert.That(McpSchemaInspector.HasType(item, McpSchemaInspector.String)).IsTrue();
        await Assert.That(McpSchemaInspector.HasType(item, McpSchemaInspector.Null)).IsFalse();
    }

    internal static async Task RequireReceiptOrPlacementAsync(JsonElement root, JsonElement body, string[] fields)
    {
        if (fields.Contains(Position, StringComparer.Ordinal))
        {
            await RequireStringsAsync(root, body, [Incarnation, McpMovementCatalogProtocol.Atomic]);
            await LeafAsync(root, body, Position, McpSchemaInspector.Integer);
            await LeafAsync(root, body, Ownership, McpSchemaInspector.Integer);
            return;
        }
        await RequireOwnerAsync(root, body);
        await LeafAsync(root, body, Version, McpSchemaInspector.Integer);
        await LeafAsync(root, body, Revision, McpSchemaInspector.Integer);
    }

    private static async Task EnumAsync(JsonElement root, JsonElement body, string field, string[] names, bool input)
    {
        var value = Resolve(root, body.GetProperty(McpMovementCatalogProtocol.Properties).GetProperty(field));
        var branches = value.GetProperty(McpSchemaInspector.AnyOf).EnumerateArray().ToArray();
        await Assert.That(branches.Length).IsEqualTo(2);
        string[] stringKeys = input ? [McpSchemaInspector.Type, McpSchemaInspector.Examples] : [McpSchemaInspector.Enum];
        await Assert.That(branches[0].EnumerateObject().Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal).SetEquals(stringKeys)).IsTrue();
        if (input)
        { await Assert.That(branches[0].GetProperty(McpSchemaInspector.Type).GetString()).IsEqualTo(McpSchemaInspector.String); }
        var actualNames = branches[0].GetProperty(input ? McpSchemaInspector.Examples : McpSchemaInspector.Enum)
            .EnumerateArray().Select(name => name.GetString()!).ToArray();
        await Assert.That(actualNames.Length).IsEqualTo(names.Length);
        await Assert.That(actualNames.ToHashSet(StringComparer.Ordinal).SetEquals(names)).IsTrue();
        await Assert.That(branches[1].GetProperty(McpSchemaInspector.Type).GetString()).IsEqualTo(McpSchemaInspector.Integer);
        await Assert.That(branches[1].GetProperty(McpMovementCatalogProtocol.Minimum).GetInt32()).IsEqualTo(int.MinValue);
        await Assert.That(branches[1].GetProperty(McpMovementCatalogProtocol.Maximum).GetInt32()).IsEqualTo(int.MaxValue);
        await Assert.That(McpSchemaInspector.HasType(value, McpSchemaInspector.Null)).IsFalse();
    }

    private static async Task LeafAsync(JsonElement root, JsonElement body, string field, string type)
    {
        var value = Resolve(root, body.GetProperty(McpMovementCatalogProtocol.Properties).GetProperty(field));
        await Assert.That(McpSchemaInspector.HasType(value, type)).IsTrue();
        await Assert.That(McpSchemaInspector.HasType(value, McpSchemaInspector.Null)).IsFalse();
    }

    private static JsonElement Resolve(JsonElement root, JsonElement value) =>
        value.TryGetProperty(McpSchemaInspector.Ref, out var reference)
            ? McpSchemaInspector.Resolve(root, reference.GetString()!) : value;
}
