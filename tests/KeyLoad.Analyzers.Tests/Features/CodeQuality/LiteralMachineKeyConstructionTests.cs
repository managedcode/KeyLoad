using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-009: construction keys are distinct from human values.</summary>
internal sealed class LiteralMachineKeyConstructionTests
{
    private const string DiagnosticId = LiteralMachineKeyAnalyzer.DiagnosticId;

    [Test]
    public async Task KeyValuePairAndDictionaryEntryKeysAreDiagnosedAsync()
    {
        const string pairKey = "\"pair-key\"";
        const string pairNamedKey = "\"pair-named-key\"";
        const string entryKey = "\"entry-key\"";
        const string entryNamedKey = "\"entry-named-key\"";
        const string source = """
            using System.Collections;
            using System.Collections.Generic;
            internal static class Subject
            {
                internal static void Create()
                {
                    _ = new KeyValuePair<string, string>("pair-key", "pair-value");
                    _ = new KeyValuePair<string, string>(value: "pair-human-value", key: "pair-named-key");
                    _ = new DictionaryEntry("entry-key", "entry-value");
                    _ = new DictionaryEntry(value: "entry-human-value", key: "entry-named-key");
                }
            }
            """;

        var findings = await AnalyzerFixture.AnalyzeAsync(new LiteralMachineKeyAnalyzer(), source);
        await AssertFindingsAtTokensAsync(findings, source, pairKey, pairNamedKey, entryKey, entryNamedKey);
    }

    [Test]
    public async Task DictionaryInitializerIsDiagnosedButTupleValuesAreNotAsync()
    {
        const string initializerKey = "\"initializer-key\"";
        const string source = """
            using System;
            using System.Collections.Generic;
            internal static class Subject
            {
                internal static void Create()
                {
                    var values = new Dictionary<string, int> { { "initializer-key", 7 } };
                    _ = new Tuple<string, string>("tuple-human-first", "tuple-human-second");
                }
            }
            """;

        var findings = await AnalyzerFixture.AnalyzeAsync(new LiteralMachineKeyAnalyzer(), source);
        await AssertFindingsAtTokensAsync(findings, source, initializerKey);
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
            await Assert.That(finding.Id).IsEqualTo(DiagnosticId);
            await Assert.That(finding.Severity).IsEqualTo(DiagnosticSeverity.Error);
            await Assert.That(finding.Location.IsInSource).IsTrue();
            await Assert.That(finding.Location.SourceTree?.FilePath).IsEqualTo(AnalyzerFixture.ContractPath);
            await Assert.That(finding.Location.SourceSpan.Length).IsEqualTo(token.Length);
        }
    }
}
