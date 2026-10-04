using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-008: checks source spans and trivia within executable units.</summary>
internal sealed class ExecutableUnitSpanAndTriviaAnalyzerTests
{
    private const string Id = "KLD0032";
    private const string Path = "/repo/src/Subject.cs";

    [Test]
    public async Task MultilineLiteralLinesCountWithinTheExecutableUnitAsync()
    {
        var literalLines = Enumerable.Range(0, 52)
            .Select(static index => index == 10
                ? "            value { 1 + 2 }"
                : $"            line{index}");
        var source = "internal sealed class Subject\n{\n    internal static string Read()\n    {\n" +
            "        return $\"\"\"\n" + string.Join("\n", literalLines) +
            "\n            \"\"\";\n    }\n}";
        var findings = await NumericAnalyzerFixture.AnalyzeAsync(
            new ExecutableUnitCodeLineCountAnalyzer(), (Path, source));
        await NumericAnalyzerFixture.AssertSingleFindingAsync(
            findings, Id, DiagnosticSeverity.Error, Path, line: 2);
    }

    [Test]
    public async Task ExpressionBodiedPropertyAndIndexerAreMeasuredAsUnitsAsync()
    {
        var literalLines = string.Join("\n", Enumerable.Range(0, 52)
            .Select(static index => $"        value{index}"));
        var source = "internal sealed class Subject\n{\n" +
            "    internal string Value => \"\"\"\n" + literalLines + "\n    \"\"\";\n" +
            "    internal string this[int index] => \"\"\"\n" + literalLines + "\n    \"\"\";\n}";
        var findings = await NumericAnalyzerFixture.AnalyzeAsync(
            new ExecutableUnitCodeLineCountAnalyzer(), (Path, source));
        await Assert.That(findings.Length).IsEqualTo(2);
        await Assert.That(findings.All(finding =>
            finding.Id == Id && finding.Severity == DiagnosticSeverity.Error &&
            finding.Location.SourceTree?.FilePath == Path)).IsTrue();
        var locations = findings.Select(static finding => finding.Location.GetLineSpan().StartLinePosition)
            .OrderBy(static position => position.Line)
            .ToArray();
        await Assert.That(locations[0].Line).IsEqualTo(2);
        await Assert.That(locations[0].Character).IsEqualTo(4);
        await Assert.That(locations[1].Line).IsEqualTo(56);
        await Assert.That(locations[1].Character).IsEqualTo(4);
    }

    [Test]
    public async Task DisabledTextInsideExecutableUnitContributesToItsTotalAsync()
    {
        var statements = CreateStatements(43, "        ");
        var disabled = string.Join("\n", Enumerable.Range(0, 7)
            .Select(static index => $"disabled line {index}"));
        var source = "internal sealed class Subject\n{\n    internal static void Run()\n    {\n" +
            statements + "\n#if NEVER_DEFINED\n" + disabled +
            "\n#endif\n    }\n}";
        var findings = await NumericAnalyzerFixture.AnalyzeAsync(
            new ExecutableUnitCodeLineCountAnalyzer(), (Path, source));
        var methodStart = source.IndexOf("internal static void Run()", StringComparison.Ordinal);
        var methodEnd = source.LastIndexOf("\n}", StringComparison.Ordinal);
        await NumericAnalyzerFixture.AssertSingleFindingAsync(
            findings, Id, DiagnosticSeverity.Error, Path, line: 2, column: 4,
            span: TextSpan.FromBounds(methodStart, methodEnd));
    }

    [Test]
    public async Task MultilineCommentInsideExecutableUnitAddsNoCodeLinesAsync()
    {
        var statements = CreateStatements(47, "        ");
        var source = "internal sealed class Subject\n{\n    internal static void Run()\n    {\n" +
            statements + "\n        /*\n            comment line one\n            comment line two\n" +
            "            comment line three\n        */\n    }\n}";
        var findings = await NumericAnalyzerFixture.AnalyzeAsync(
            new ExecutableUnitCodeLineCountAnalyzer(), (Path, source));
        await Assert.That(findings).IsEmpty();
    }

    private static string CreateStatements(int count, string indentation = "                ") => string.Join(
        "\n",
        Enumerable.Range(0, count).Select(index =>
            $"{indentation}System.Console.WriteLine({index});"));
}
