using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-BC-027 and AC-CQ-009: construction binds keys independently from value positions.</summary>
internal sealed class LiteralMachineKeyCreationBindingTests
{
    [Test]
    public async Task KeyValuePairAndDictionaryEntryPositionalKeysAreDiagnosedAsync()
    {
        const string pairKey = "\"pair-key\"";
        const string entryKey = "\"entry-key\"";
        const string source = """
            using System.Collections;
            using System.Collections.Generic;
            internal static class Subject
            {
                internal static void Create()
                {
                    _ = new KeyValuePair<string, string>("pair-key", "pair-value");
                    _ = new DictionaryEntry("entry-key", "entry-value");
                }
            }
            """;
        var findings = await AnalyzerFixture.AnalyzeAsync(new LiteralMachineKeyAnalyzer(), source);
        await AssertFindingsAtTokensAsync(findings, source, pairKey, entryKey);
    }

    [Test]
    public async Task ReorderedNamedConstructorsBindKeysNotFirstSourceValuesAsync()
    {
        const string pairKey = "\"pair-key\"";
        const string entryKey = "\"entry-key\"";
        const string source = """
            using System.Collections;
            using System.Collections.Generic;
            internal static class Subject
            {
                internal static void Create()
                {
                    _ = new KeyValuePair<string, string>(value: "pair-value", key: "pair-key");
                    _ = new DictionaryEntry(value: "entry-value", key: "entry-key");
                }
            }
            """;
        var findings = await AnalyzerFixture.AnalyzeAsync(new LiteralMachineKeyAnalyzer(), source);
        await AssertFindingsAtTokensAsync(findings, source, pairKey, entryKey);
    }

    [Test]
    public async Task DictionaryComplexInitializerKeyIsDiagnosedAsync()
    {
        const string initializerKey = "\"initializer-key\"";
        const string source = """
            using System.Collections.Generic;
            internal static class Subject
            {
                internal static Dictionary<string, int> Create() => new()
                {
                    { "initializer-key", 7 }
                };
            }
            """;
        var findings = await AnalyzerFixture.AnalyzeAsync(new LiteralMachineKeyAnalyzer(), source);
        await AssertFindingsAtTokensAsync(findings, source, initializerKey);
    }

    [Test]
    public async Task OrdinaryListAndTupleTextIsNotAKeyAsync()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            internal static class Subject
            {
                internal static void Create()
                {
                    var values = new List<string>();
                    values.Add("list-value");
                    _ = new Tuple<string, string>("tuple-first", "tuple-second");
                }
            }
            """;
        var findings = await AnalyzerFixture.AnalyzeAsync(new LiteralMachineKeyAnalyzer(), source);
        await Assert.That(findings).IsEmpty();
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
