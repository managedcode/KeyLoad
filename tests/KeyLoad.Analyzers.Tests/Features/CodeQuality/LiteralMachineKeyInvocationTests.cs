using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-BC-027 and AC-CQ-009: machine-key invocations distinguish key from value arguments.</summary>
internal sealed class LiteralMachineKeyInvocationTests
{
    [Test]
    public async Task Utf8JsonWriterValueArgumentIsNotAMachineKeyAsync()
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
        await AnalyzerFixture.AssertFindingAsync(
            findings,
            LiteralMachineKeyAnalyzer.DiagnosticId,
            DiagnosticSeverity.Error,
            source,
            "property-name");
    }

    [Test]
    public async Task DictionaryAndHeaderInvocationsDiagnoseKeysNotValuesAsync()
    {
        const string positionalKey = "\"dictionary-key\"";
        const string namedKey = "\"named-dictionary-key\"";
        const string headerKey = "\"header-name\"";
        const string writerKey = "\"writer-property\"";
        const string source = """
            using System.Collections.Generic;
            using System.Collections.Specialized;
            using System.Text.Json;
            internal static class Subject
            {
                internal static void Run(Dictionary<string, string> values, NameValueCollection headers, Utf8JsonWriter writer)
                {
                    values.Add("dictionary-key", "dictionary-human-value");
                    values.Add(value: "named-human-value", key: "named-dictionary-key");
                    headers.Add("header-name", "header-human-value");
                    writer.WriteString("writer-property", "writer-human-value");
                }
            }
            """;

        var findings = await AnalyzerFixture.AnalyzeAsync(new LiteralMachineKeyAnalyzer(), source);
        await AssertFindingsAtTokensAsync(findings, source, positionalKey, namedKey, headerKey, writerKey);
    }

    [Test]
    public async Task JsonPropertyLookupsDiagnoseDirectAndNestedKeysAsync()
    {
        const string directKey = "\"json-property\"";
        const string nestedPrefix = "\"prefix\"";
        const string source = """
            using System.Text.Json;
            internal static class Subject
            {
                internal static void Read(JsonElement element, string suffix)
                {
                    _ = element.GetProperty("json-property");
                    _ = element.GetProperty("prefix" + suffix);
                }
            }
            """;

        var findings = await AnalyzerFixture.AnalyzeAsync(new LiteralMachineKeyAnalyzer(), source);
        await AssertFindingsAtTokensAsync(findings, source, directKey, nestedPrefix);
    }

    [Test]
    public async Task ConstantEmptyListAndHumanValueLiteralsAreAllowedAsync()
    {
        const string key = "\"writer-property\"";
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Text.Json;
            internal static class Subject
            {
                internal static void Run(Dictionary<string, string> values, Utf8JsonWriter writer)
                {
                    const string key = "constant-key";
                    values.Add(key, "dictionary-human-value");
                    values.Add("", "empty-key-value");
                    var list = new List<string>();
                    list.Add("list-human-value");
                    Console.WriteLine("human-message");
                    writer.WriteString("writer-property", "writer-human-value");
                }
            }
            """;

        var findings = await AnalyzerFixture.AnalyzeAsync(new LiteralMachineKeyAnalyzer(), source);
        await AssertFindingsAtTokensAsync(findings, source, key);
    }

    private static async Task AssertFindingsAtTokensAsync(
        ImmutableArray<Diagnostic> findings,
        string source,
        params string[] expectedTokens)
    {
        await Assert.That(findings.Length).IsEqualTo(expectedTokens.Length);
        foreach (var token in expectedTokens)
        {
            var start = source.IndexOf(token, StringComparison.Ordinal);
            await Assert.That(start >= 0).IsTrue();
            var finding = findings.Single(item => item.Location.SourceSpan.Start == start);
            await Assert.That(finding.Id).IsEqualTo(LiteralMachineKeyAnalyzer.DiagnosticId);
            await Assert.That(finding.Severity).IsEqualTo(DiagnosticSeverity.Error);
            await Assert.That(finding.Location.IsInSource).IsTrue();
            await Assert.That(finding.Location.SourceTree?.FilePath).IsEqualTo(AnalyzerFixture.ContractPath);
            await Assert.That(finding.Location.SourceSpan.Length).IsEqualTo(token.Length);
        }
    }
}
