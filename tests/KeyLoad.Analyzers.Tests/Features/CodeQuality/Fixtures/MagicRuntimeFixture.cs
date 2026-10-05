using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

internal static class MagicRuntimeFixture
{
    private const string MarkerOpen = "[|";
    private const string MarkerClose = "|]";
    private const string MarkerNotClosedMessage = "The diagnostic token marker is not closed.";

    internal static Task AssertDurationAsync(
        string markedSource,
        string assemblyName = AnalyzerFixture.ServerAssembly,
        string path = AnalyzerFixture.ContractPath) =>
        AssertAsync(new MagicRuntimeDurationAnalyzer(), MagicRuntimeDurationAnalyzer.DiagnosticId,
            markedSource, assemblyName, path);

    internal static Task AssertStringAsync(
        string markedSource,
        string assemblyName = AnalyzerFixture.ServerAssembly,
        string path = AnalyzerFixture.ContractPath) =>
        AssertAsync(new MagicRuntimeStringAnalyzer(), MagicRuntimeStringAnalyzer.DiagnosticId,
            markedSource, assemblyName, path);

    internal static Task AssertConfigurationAsync(
        string markedSource,
        string assemblyName = AnalyzerFixture.ServerAssembly,
        string path = AnalyzerFixture.ContractPath) =>
        AssertAsync(new TypedConfigurationAnalyzer(), TypedConfigurationAnalyzer.DiagnosticId,
            markedSource, assemblyName, path);

    private static async Task AssertAsync(
        DiagnosticAnalyzer analyzer,
        string diagnosticId,
        string markedSource,
        string assemblyName,
        string path)
    {
        var (source, expectedSpans) = Unmark(markedSource);
        var findings = await AnalyzerFixture.AnalyzeAsync(analyzer, source, assemblyName, path);
        var ordered = findings.OrderBy(static diagnostic => diagnostic.Location.SourceSpan.Start).ToArray();
        await Assert.That(ordered.Length).IsEqualTo(expectedSpans.Length);
        for (var index = 0; index < ordered.Length; index++)
        {
            var finding = ordered[index];
            await Assert.That(finding.Id).IsEqualTo(diagnosticId);
            await Assert.That(finding.Severity).IsEqualTo(DiagnosticSeverity.Error);
            await Assert.That(finding.Location.IsInSource).IsTrue();
            await Assert.That(finding.Location.SourceTree?.FilePath).IsEqualTo(path);
            await Assert.That(finding.Location.SourceSpan).IsEqualTo(expectedSpans[index]);
            var sourceText = await finding.Location.SourceTree!.GetTextAsync();
            await Assert.That(sourceText.ToString(finding.Location.SourceSpan))
                .IsEqualTo(source.Substring(expectedSpans[index].Start, expectedSpans[index].Length));
        }
    }

    private static (string Source, ImmutableArray<TextSpan> Spans) Unmark(string markedSource)
    {
        var source = new StringBuilder();
        var spans = ImmutableArray.CreateBuilder<TextSpan>();
        var cursor = 0;
        while (markedSource.IndexOf(MarkerOpen, cursor, StringComparison.Ordinal) is var start && start >= 0)
        {
            source.Append(markedSource, cursor, start - cursor);
            var valueStart = start + MarkerOpen.Length;
            var end = markedSource.IndexOf(MarkerClose, valueStart, StringComparison.Ordinal);
            if (end < 0)
            {
                throw new InvalidOperationException(MarkerNotClosedMessage);
            }

            var length = end - valueStart;
            spans.Add(new TextSpan(source.Length, length));
            source.Append(markedSource, valueStart, length);
            cursor = end + MarkerClose.Length;
        }

        source.Append(markedSource, cursor, markedSource.Length - cursor);
        return (source.ToString(), spans.ToImmutable());
    }
}
