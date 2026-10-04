using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-008: checks the file code-line diagnostic at its exact boundaries.</summary>
internal sealed class FileCodeLineCountAnalyzerTests
{
    private const string Id = "KLD0030";
    private const string Path = "/repo/src/Subject.cs";

    [Test]
    public async Task FileCodeLinesAtLimitPassAndOneOverReportsAsync()
    {
        foreach (var count in new[] { 399, 400 })
        {
            var source = CreateTypes(count);
            var findings = await NumericAnalyzerFixture.AnalyzeAsync(
                new FileCodeLineCountAnalyzer(), (Path, source));
            await Assert.That(findings).IsEmpty();
        }

        var over = CreateTypes(401);
        var violation = await NumericAnalyzerFixture.AnalyzeAsync(
            new FileCodeLineCountAnalyzer(), (Path, over));
        await NumericAnalyzerFixture.AssertSingleFindingAsync(
            violation, Id, DiagnosticSeverity.Error, Path, line: 0, column: 0,
            span: new TextSpan(0, over.Length));
    }

    [Test]
    public async Task CommentsAndBlankLinesDoNotAddCodeLinesAsync()
    {
        var source = CreateTypes(400) +
            "\n\n// comment only\n/// <summary>XML documentation</summary>\n/*\n" +
            "block comment line one\nblock comment line two\n*/\n";
        var findings = await NumericAnalyzerFixture.AnalyzeAsync(
            new FileCodeLineCountAnalyzer(), (Path, source));
        await Assert.That(findings).IsEmpty();
    }

    [Test]
    public async Task GeneratedSourceIsIgnoredByRoslynPolicyAsync()
    {
        var source = CreateTypes(401);
        var findings = await NumericAnalyzerFixture.AnalyzeAsync(
            new FileCodeLineCountAnalyzer(), ("/repo/src/Subject.g.cs", source));
        await Assert.That(findings).IsEmpty();
    }

    [Test]
    public async Task NonblankDirectiveAndDisabledTextLinesCountAsync()
    {
        var source = CreateTypes(398) +
            "\n#if NEVER_DEFINED\ninternal sealed class Disabled { }\n#endif\n";
        var findings = await NumericAnalyzerFixture.AnalyzeAsync(
            new FileCodeLineCountAnalyzer(), (Path, source));
        await NumericAnalyzerFixture.AssertSingleFindingAsync(
            findings, Id, DiagnosticSeverity.Error, Path, line: 0);
    }

    [Test]
    public async Task OrdinaryAndRawMultilineLiteralTokensCountEachPhysicalLineAsync()
    {
        var ordinary = "internal sealed class Subject { internal static string Read() => \"first\\n\" +\n" +
            "    \"second\"; }";
        var ordinaryFindings = await NumericAnalyzerFixture.AnalyzeAsync(
            new FileCodeLineCountAnalyzer(), (Path, ordinary));
        await Assert.That(ordinaryFindings).IsEmpty();

        var rawLines = Enumerable.Range(0, 399).Select(static index => $"value{index}");
        var raw = "internal static class Subject { internal static string Value = \"\"\"\n" +
            string.Join("\n", rawLines) + "\n\"\"\"; }";
        var rawFindings = await NumericAnalyzerFixture.AnalyzeAsync(
            new FileCodeLineCountAnalyzer(), (Path, raw));
        await NumericAnalyzerFixture.AssertSingleFindingAsync(
            rawFindings, Id, DiagnosticSeverity.Error, Path, line: 0, column: 0,
            span: new TextSpan(0, raw.Length));
    }

    private static string CreateTypes(int count) => string.Join(
        "\n",
        Enumerable.Range(0, count).Select(static index => $"internal sealed class C{index} {{ }}"));
}
