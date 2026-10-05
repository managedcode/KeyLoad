using Microsoft.CodeAnalysis;

namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

internal static class TypedSynchronizationFixture
{
    internal static async Task AssertFindingAsync(string source, string expression)
    {
        var findings = await AnalyzerFixture.AnalyzeAsync(new TypedSynchronizationAnalyzer(), source);
        await AnalyzerFixture.AssertFindingAsync(findings, TypedSynchronizationAnalyzer.DiagnosticId,
            DiagnosticSeverity.Error, source, expression);
        var finding = findings[0];
        await Assert.That(finding.Location.SourceSpan.Start)
            .IsEqualTo(source.IndexOf(expression, StringComparison.Ordinal));
        await Assert.That(finding.Location.SourceSpan.Length).IsEqualTo(expression.Length);
        await Assert.That(source.Substring(finding.Location.SourceSpan.Start, finding.Location.SourceSpan.Length))
            .IsEqualTo(expression);
    }

    internal static async Task AssertAllowedAsync(string source, string path = AnalyzerFixture.ContractPath)
    {
        var findings = await AnalyzerFixture.AnalyzeAsync(new TypedSynchronizationAnalyzer(), source, path: path);
        await Assert.That(findings).IsEmpty();
    }
}
