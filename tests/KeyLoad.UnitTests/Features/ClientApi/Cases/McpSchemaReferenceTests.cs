using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCP-003/007: actual schema-local references and caller JSON data are kept distinct.</summary>
internal sealed class McpSchemaReferenceTests
{
    private const string RefFixture = "{\"properties\":{\"value\":{\"$ref\":\"#/properties/value\"}},\"default\":{\"$ref\":\"#/application/value\"},\"examples\":[{\"$ref\":\"#/application/example\"}]}";
    private const string Value = "value";
    private const string RefPrefix = "#/$defs/request";
    private const string ExpectedSchemaRef = "#/$defs/request/properties/value";
    private const string ExpectedDataRef = "#/application/value";
    private const string ExpectedExampleRef = "#/application/example";

    /// <summary>Every native reference resolves after its canonical graph is embedded in the wrapper.</summary>
    [Test]
    public async Task AcMcp003AllCatalogSchemaReferencesResolveInsideTheirWrappedDocuments()
    {
        foreach (var descriptor in McpOperationCatalog.Entries)
        {
            foreach (var pointer in McpSchemaInspector.References(descriptor.InputSchema))
            {
                await Assert.That(McpSchemaInspector.Resolve(descriptor.InputSchema, pointer).ValueKind)
                    .IsNotEqualTo(JsonValueKind.Undefined);
            }
            foreach (var pointer in McpSchemaInspector.References(descriptor.OutputSchema))
            {
                await Assert.That(McpSchemaInspector.Resolve(descriptor.OutputSchema, pointer).ValueKind)
                    .IsNotEqualTo(JsonValueKind.Undefined);
            }
        }
    }

    /// <summary>Only schema references are relocated; user default and example references remain exact.</summary>
    [Test]
    public async Task AcMcp003ReferenceRebasingNeverChangesApplicationDefaultOrExampleData()
    {
        var schema = JsonNode.Parse(RefFixture)!;
        McpSchemaReferences.Rebase(schema, RefPrefix);
        await Assert.That(schema[McpSchemaInspector.Properties]![Value]![McpSchemaInspector.Ref]!.GetValue<string>())
            .IsEqualTo(ExpectedSchemaRef);
        await Assert.That(schema[McpSchemaInspector.Default]![McpSchemaInspector.Ref]!.GetValue<string>()).IsEqualTo(ExpectedDataRef);
        await Assert.That(schema[McpSchemaInspector.Examples]![0]![McpSchemaInspector.Ref]!.GetValue<string>()).IsEqualTo(ExpectedExampleRef);
    }
}
