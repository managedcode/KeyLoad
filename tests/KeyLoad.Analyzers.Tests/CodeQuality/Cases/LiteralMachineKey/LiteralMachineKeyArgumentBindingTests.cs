using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-BC-027 and AC-CQ-009: invocation binding distinguishes actual keys from values.</summary>
internal sealed class LiteralMachineKeyArgumentBindingTests
{
    private const string PropertyNameLiteral = "\"property-name\"";

    [Test]
    public async Task Utf8JsonWriterPositionalArgumentsBindPropertyAndValueAsync()
    {
        const string source = """
            using System.Text.Json;
            internal static class Subject
            {
                internal static void Write(Utf8JsonWriter writer)
                {
                    writer.WriteString("property-name", "human-value");
                }
            }
            """;
        var findings = await AnalyzerFixture.AnalyzeAsync(new LiteralMachineKeyAnalyzer(), source);
        await AssertFindingsAtTokensAsync(findings, source, PropertyNameLiteral);
    }

    [Test]
    public async Task Utf8JsonWriterReorderedNamedArgumentsBindPropertyAndValueAsync()
    {
        const string source = """
            using System.Text.Json;
            internal static class Subject
            {
                internal static void Write(Utf8JsonWriter writer)
                {
                    writer.WriteString(value: "human-value", propertyName: "property-name");
                }
            }
            """;
        var findings = await AnalyzerFixture.AnalyzeAsync(new LiteralMachineKeyAnalyzer(), source);
        await AssertFindingsAtTokensAsync(findings, source, PropertyNameLiteral);
    }

    [Test]
    public async Task DictionaryAddBindsPositionalAndReorderedNamedKeysAsync()
    {
        const string positionalKey = "\"positional-key\"";
        const string reorderedKey = "\"reordered-key\"";
        const string source = """
            using System.Collections.Generic;
            internal static class Subject
            {
                internal static void Add(Dictionary<string, string> values)
                {
                    values.Add("positional-key", "positional-value");
                    values.Add(value: "reordered-value", key: "reordered-key");
                }
            }
            """;
        var findings = await AnalyzerFixture.AnalyzeAsync(new LiteralMachineKeyAnalyzer(), source);
        await AssertFindingsAtTokensAsync(findings, source, positionalKey, reorderedKey);
    }

    [Test]
    public async Task ConstantDictionaryKeyDoesNotMakeValueLiteralAMachineKeyAsync()
    {
        const string source = """
            using System.Collections.Generic;
            internal static class Subject
            {
                internal static void Add(Dictionary<string, string> values)
                {
                    const string key = "declared-key";
                    values.Add(key: key, value: "human-value");
                }
            }
            """;
        var findings = await AnalyzerFixture.AnalyzeAsync(new LiteralMachineKeyAnalyzer(), source);
        await Assert.That(findings).IsEmpty();
    }

    [Test]
    public async Task NestedKeyConcatIsPositiveAndNestedValueConcatIsNegativeAsync()
    {
        const string keyPrefix = "\"key-prefix\"";
        const string writerKey = "\"writer-property\"";
        const string source = """
            using System.Text.Json;
            internal static class Subject
            {
                internal static void ReadAndWrite(JsonElement element, Utf8JsonWriter writer, string suffix)
                {
                    _ = element.GetProperty("key-prefix" + suffix);
                    writer.WriteString("writer-property", "value-prefix" + suffix);
                }
            }
            """;
        var findings = await AnalyzerFixture.AnalyzeAsync(new LiteralMachineKeyAnalyzer(), source);
        await AssertFindingsAtTokensAsync(findings, source, keyPrefix, writerKey);
    }

    [Test]
    public async Task DynamicPropertyLookupUsesNamedAndPositionalKeyFallbackAsync()
    {
        const string namedKey = "\"named-key\"";
        const string positionalKey = "\"positional-key\"";
        const string source = """
            internal static class Subject
            {
                internal static void Read(dynamic element)
                {
                    _ = element.GetProperty(propertyName: "named-key");
                    _ = element.GetProperty("positional-key");
                    _ = element.GetProperty(value: "human-value");
                }
            }
            """;
        var findings = await AnalyzerFixture.AnalyzeAsync(new LiteralMachineKeyAnalyzer(), source);
        await AssertFindingsAtTokensAsync(findings, source, namedKey, positionalKey);
    }

    [Test]
    public async Task ReducedAndStaticEndpointExtensionsBindNamesAndExpandedTagsAsync()
    {
        const string reducedName = "\"reduced-name\"";
        const string staticName = "\"static-name\"";
        const string reducedFirstTag = "\"reduced-tag-one\"";
        const string reducedSecondTag = "\"reduced-tag-two\"";
        const string staticFirstTag = "\"static-tag-one\"";
        const string staticSecondTag = "\"static-tag-two\"";
        const string source = """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            internal static class Subject
            {
                internal static void Configure(IEndpointConventionBuilder builder)
                {
                    builder.WithName("reduced-name");
                    RoutingEndpointConventionBuilderExtensions.WithName<IEndpointConventionBuilder>(builder, "static-name");
                    builder.WithTags("reduced-tag-one", "reduced-tag-two");
                    OpenApiRouteHandlerBuilderExtensions.WithTags<IEndpointConventionBuilder>(builder, "static-tag-one", "static-tag-two");
                }
            }
            """;
        var findings = await AnalyzerFixture.AnalyzeAsync(new LiteralMachineKeyAnalyzer(), source);
        await AssertFindingsAtTokensAsync(findings, source, reducedName, staticName, reducedFirstTag, reducedSecondTag, staticFirstTag, staticSecondTag);
    }

    private static async Task AssertFindingsAtTokensAsync(
        ImmutableArray<Diagnostic> findings,
        string source,
        params string[] expectedTokens)
    {
        await Assert.That(findings.Length).IsEqualTo(expectedTokens.Length);
        foreach (var expectedToken in expectedTokens)
        {
            var expectedStart = source.IndexOf(expectedToken, StringComparison.Ordinal);
            await Assert.That(expectedStart >= 0).IsTrue();
            var finding = findings.Single(diagnostic => diagnostic.Location.SourceSpan.Start == expectedStart);
            await Assert.That(finding.Id).IsEqualTo(LiteralMachineKeyAnalyzer.DiagnosticId);
            await Assert.That(finding.Severity).IsEqualTo(DiagnosticSeverity.Error);
            await Assert.That(finding.Location.IsInSource).IsTrue();
            await Assert.That(finding.Location.SourceTree?.FilePath).IsEqualTo(AnalyzerFixture.ContractPath);
            await Assert.That(finding.Location.SourceSpan.Length).IsEqualTo(expectedToken.Length);
        }
    }
}
