using System.Text.Json;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCP-003: actual native enum parser retains numeric, nullable and flags forms.</summary>
internal sealed class McpSchemaEnumTests
{
    private const string MetricKey = "metric";
    private const string OptionalKey = "optional";
    private const string GrantsKey = "grants";
    private const string OptionalGrantsKey = "optionalGrants";
    private const string Minimum = "minimum";
    private const string Maximum = "maximum";
    private const string NumericText = "1";
    private const string MixedCaseText = " cosine ";
    private const string CombinedText = "Cosine, Euclidean";
    private const string FlagsText = "DocumentsRead, DocumentsWrite";
    private const string InvalidText = "missing-metric";
    private const int NumericValue = 1;

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
        await Assert.That(numbers.GetProperty(Minimum).GetInt64()).IsEqualTo(long.MinValue);
        await Assert.That(numbers.GetProperty(Maximum).GetInt64()).IsEqualTo(long.MaxValue);
    }

    /// <summary>The real canonical parser accepts numeric strings, casing, whitespace, combinations and flags.</summary>
    [Test]
    public async Task AcMcp003CanonicalParserStillAcceptsAllExistingEnumInputForms()
    {
        await Assert.That(Parse(NumericText)).IsEqualTo(DistanceMetric.Euclidean);
        await Assert.That(Parse(MixedCaseText)).IsEqualTo(DistanceMetric.Cosine);
        await Assert.That(Parse(CombinedText)).IsEqualTo(DistanceMetric.Euclidean);
        await Assert.That(JsonSerializer.Deserialize<DistanceMetric>(NumericValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
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

    /// <summary>Output enums preserve native named forms, numeric values and nullable metadata.</summary>
    [Test]
    public async Task AcMcp007OutputSchemasRetainNativeNamesNumericFormsAndNull()
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

    private static DistanceMetric Parse(string value) =>
        JsonSerializer.Deserialize<DistanceMetric>(JsonSerializer.Serialize(value), JsonDefaults.Options);
}
