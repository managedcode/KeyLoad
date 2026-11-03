using System.Text.Json;
using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.ClientApi;

/// <summary>AC-MCP-003: actual execution enum parser retains numeric, nullable and flags forms.</summary>
internal sealed class McpSchemaEnumTests
{
    private const string MetricKey = "metric";
    private const string OptionalKey = "optional";
    private const string GrantsKey = "grants";
    private const string OptionalGrantsKey = "optionalGrants";
    private const string NumericText = "1";
    private const string MixedCaseText = " cosine ";
    private const string CombinedText = "Cosine, Euclidean";
    private const string FlagsText = "DocumentsRead, DocumentsWrite";
    private const string InvalidText = "missing-metric";

    private sealed record EnumProbe(DistanceMetric Metric, DistanceMetric? Optional, Capability Grants, Capability? OptionalGrants);

    /// <summary>Enum input schemas retain nullable strings and actual underlying integer ranges.</summary>
    [Test]
    public async Task AcMcp003InputSchemasIncludeIntegerAndStringForNullableAndFlagsEnums()
    {
        var request = McpSchemaInspector.DefinedRequest(McpSchemaFactory.CreateInput(typeof(EnumProbe), false));
        var fields = request.GetProperty(McpSchemaInspector.Properties);
        foreach (var key in new[] { MetricKey, OptionalKey, GrantsKey, OptionalGrantsKey })
        {
            await Assert.That(McpSchemaInspector.HasType(fields.GetProperty(key), McpSchemaInspector.String)).IsTrue();
            await Assert.That(McpSchemaInspector.HasType(fields.GetProperty(key), McpSchemaInspector.Integer)).IsTrue();
        }
        await Assert.That(McpSchemaInspector.HasType(fields.GetProperty(OptionalKey), McpSchemaInspector.Null)).IsTrue();
        await Assert.That(McpSchemaInspector.HasType(fields.GetProperty(OptionalGrantsKey), McpSchemaInspector.Null)).IsTrue();
        var numbers = fields.GetProperty(GrantsKey).GetProperty(McpSchemaInspector.AnyOf).EnumerateArray()
            .First(branch => McpSchemaInspector.HasType(branch, McpSchemaInspector.Integer));
        await Assert.That(numbers.GetProperty(McpSchemaInspector.Minimum).GetInt64()).IsEqualTo(long.MinValue);
        await Assert.That(numbers.GetProperty(McpSchemaInspector.Maximum).GetInt64()).IsEqualTo(long.MaxValue);
    }

    /// <summary>The real canonical parser accepts numeric strings, casing, whitespace, combinations and flags.</summary>
    [Test]
    public async Task AcMcp003CanonicalParserStillAcceptsAllExistingEnumInputForms()
    {
        await Assert.That(Parse(NumericText)).IsEqualTo(DistanceMetric.Euclidean);
        await Assert.That(Parse(MixedCaseText)).IsEqualTo(DistanceMetric.Cosine);
        await Assert.That(Parse(CombinedText)).IsEqualTo(DistanceMetric.Euclidean);
        await Assert.That(JsonSerializer.Deserialize<DistanceMetric>(1.ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonDefaults.Options)).IsEqualTo(DistanceMetric.Euclidean);
        await Assert.That(JsonSerializer.Deserialize<Capability>(JsonSerializer.Serialize(FlagsText), JsonDefaults.Options))
            .IsEqualTo(Capability.DocumentsRead | Capability.DocumentsWrite);
        await Assert.That(Assert.ThrowsExactly<JsonException>(() => Parse(InvalidText))).IsNotNull();

        var probe = new EnumProbe(DistanceMetric.Euclidean, null,
            Capability.DocumentsRead | Capability.DocumentsWrite, null);
        var canonical = JsonSerializer.Serialize(probe, JsonDefaults.Options);
        var roundTrip = JsonSerializer.Deserialize<EnumProbe>(canonical, JsonDefaults.Options)
            ?? throw new InvalidOperationException();
        await Assert.That(roundTrip).IsEqualTo(probe);
    }

    /// <summary>Output enums preserve execution named forms, numeric values and nullable metadata.</summary>
    [Test]
    public async Task AcMcp007OutputSchemasRetainNamesNumericFormsAndNull()
    {
        var output = McpSchemaFactory.CreateOutput(typeof(EnumProbe), false).GetProperty(McpSchemaInspector.Defs)
            .GetProperty(McpSchemaInspector.Result).GetProperty(McpSchemaInspector.Properties);
        var metric = output.GetProperty(MetricKey);
        await Assert.That(metric.GetProperty(McpSchemaInspector.AnyOf)[0].GetProperty(McpSchemaInspector.Enum)
            .EnumerateArray().Select(value => value.GetString() ?? throw new InvalidOperationException()))
            .IsEquivalentTo(Enum.GetNames<DistanceMetric>());
        await Assert.That(McpSchemaInspector.HasType(metric, McpSchemaInspector.Integer)).IsTrue();
        await Assert.That(McpSchemaInspector.HasType(output.GetProperty(GrantsKey), McpSchemaInspector.String)).IsTrue();
        await Assert.That(McpSchemaInspector.HasType(output.GetProperty(OptionalKey), McpSchemaInspector.Null)).IsTrue();
    }

    [Test]
    public async Task SearchTextMatchModeIsAnOptionalTypedEnumInTheActualInputSchema()
    {
        const string property = "textMatchMode";
        if (!McpOperationCatalog.TryGetTool(McpCatalogExpectations.SearchExecute, out var descriptor))
        {
            throw new InvalidOperationException(McpCatalogExpectations.SearchExecute);
        }
        var request = McpSchemaInspector.DefinedRequest(descriptor!.CreateTool().InputSchema);
        var field = request.GetProperty(McpSchemaInspector.Properties).GetProperty(property);
        var branches = field.GetProperty(McpSchemaInspector.AnyOf).EnumerateArray().ToArray();
        await Assert.That(branches.Length).IsEqualTo(2);
        await Assert.That(branches.Select(branch => branch.GetProperty(McpSchemaInspector.Type).GetString()!)
            .ToHashSet(StringComparer.Ordinal).SetEquals([McpSchemaInspector.String, McpSchemaInspector.Integer])).IsTrue();
        var strings = branches.Single(branch => branch.GetProperty(McpSchemaInspector.Type).GetString() == McpSchemaInspector.String);
        await Assert.That(strings.GetProperty(McpSchemaInspector.Examples).EnumerateArray().Select(item => item.GetString()!))
            .IsEquivalentTo(new[] { "Any", "All" });
        var numbers = branches.Single(branch => branch.GetProperty(McpSchemaInspector.Type).GetString() == McpSchemaInspector.Integer);
        await Assert.That(numbers.GetProperty(McpSchemaInspector.Minimum).GetInt32()).IsEqualTo(int.MinValue);
        await Assert.That(numbers.GetProperty(McpSchemaInspector.Maximum).GetInt32()).IsEqualTo(int.MaxValue);
        await Assert.That(field.GetProperty(McpSchemaInspector.Default).GetString()).IsEqualTo("Any");
        await Assert.That(request.GetProperty(McpSchemaInspector.Required).EnumerateArray().Select(item => item.GetString()!)
            .ToHashSet(StringComparer.Ordinal).SetEquals(["partition", "collection"])).IsTrue();
        var output = McpSchemaFactory.CreateOutput(typeof(TextMatchMode), false).GetProperty(McpSchemaInspector.Defs)
            .GetProperty(McpSchemaInspector.Result);
        var names = output.GetProperty(McpSchemaInspector.AnyOf).EnumerateArray()
            .Single(branch => branch.TryGetProperty(McpSchemaInspector.Enum, out _));
        await Assert.That(names.GetProperty(McpSchemaInspector.Enum).EnumerateArray().Select(item => item.GetString()!))
            .IsEquivalentTo(new[] { "Any", "All" });
        var any = new SearchRequest(McpCanonicalTestData.Partition, McpCanonicalTestData.Resource, "/text", "alpha beta");
        using var json = JsonDocument.Parse(JsonDefaults.Serialize(any));
        await Assert.That(json.RootElement.TryGetProperty(property, out _)).IsFalse();
        await Assert.That(JsonDefaults.Deserialize<SearchRequest>(JsonDefaults.Serialize(any)).TextMatchMode)
            .IsEqualTo(TextMatchMode.Any);
        var all = any with { TextMatchMode = TextMatchMode.All };
        await Assert.That(JsonDefaults.Deserialize<SearchRequest>(JsonDefaults.Serialize(all)).TextMatchMode)
            .IsEqualTo(TextMatchMode.All);
    }

    private static DistanceMetric Parse(string value) =>
        JsonSerializer.Deserialize<DistanceMetric>(JsonSerializer.Serialize(value), JsonDefaults.Options);
}
