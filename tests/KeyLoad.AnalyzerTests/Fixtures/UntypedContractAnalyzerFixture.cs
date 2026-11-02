using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KeyLoad.AnalyzerTests.CodeQuality;

internal static class UntypedContractAnalyzerFixture
{
    internal const string ContractId = UntypedDataContractAnalyzer.ContractDiagnosticId;
    internal const string ConversionId = ObjectValueConversionAnalyzer.DiagnosticId;
    internal const string Reason = "The CLR contract returns runtime metadata whose declared type is checked here.";
    private static readonly ImmutableDictionary<string, ReportDiagnostic> Warnings =
        ImmutableDictionary<string, ReportDiagnostic>.Empty.Add(ContractId, ReportDiagnostic.Warn)
            .Add(ConversionId, ReportDiagnostic.Warn);

    internal static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(DiagnosticAnalyzer analyzer, string source,
        string path = AnalyzerFixture.ContractPath)
        => AnalyzerFixture.AnalyzeAsync(analyzer, source, path: path, diagnosticOptions: Warnings);

    internal static Task<ImmutableArray<Diagnostic>> ContractsAsync(string source)
        => AnalyzeAsync(new UntypedDataContractAnalyzer(), source);

    internal static Task<ImmutableArray<Diagnostic>> ConversionsAsync(string source)
        => AnalyzeAsync(new ObjectValueConversionAnalyzer(), source);

    internal static async Task LocatedAsync(ImmutableArray<Diagnostic> findings, string id, string source,
        params string[] spans)
    {
        await Assert.That(findings.Length).IsEqualTo(spans.Length);
        await Assert.That(findings.Select(finding => finding.Id).Distinct()).IsEquivalentTo([id]);
        foreach (var finding in findings)
        {
            await Assert.That(finding.Severity).IsEqualTo(DiagnosticSeverity.Warning);
            await Assert.That(finding.Location.SourceTree!.FilePath).IsEqualTo(AnalyzerFixture.ContractPath);
        }
        var actual = findings.OrderBy(finding => finding.Location.SourceSpan.Start)
            .Select(finding => source.Substring(finding.Location.SourceSpan.Start, finding.Location.SourceSpan.Length));
        await Assert.That(actual).IsEquivalentTo(spans);
    }
}
