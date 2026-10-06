using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-008: checks full executable-unit spans and nested executable units.</summary>
internal sealed class ExecutableUnitCodeLineCountAnalyzerTests
{
    private const string Id = "KLD0032";
    private const string Path = "/repo/src/Subject.cs";

    [Test]
    public async Task ExecutableUnitLinesAtLimitPassAndOneOverReportsAsync()
    {
        foreach (var count in new[] { 63, 64, 65 })
        {
            var source = CreateMethod(count);
            var findings = await NumericAnalyzerFixture.AnalyzeAsync(
                new ExecutableUnitCodeLineCountAnalyzer(), (Path, source));
            if (count <= 64)
            {
                await Assert.That(findings).IsEmpty();
            }
            else
            {
                await NumericAnalyzerFixture.AssertSingleFindingAsync(
                    findings, Id, DiagnosticSeverity.Error, Path, line: 2, column: 4,
                    span: MethodSpan(source));
                await Assert.That(findings[0].GetMessage(CultureInfo.InvariantCulture))
                    .IsEqualTo("Executable unit contains 65 code lines; the maximum is 64");
            }
        }
    }

    [Test]
    public async Task AccessorLocalFunctionAndLambdaAreMeasuredSeparatelyAsync()
    {
        const string source = """
            internal sealed class Subject
            {
                internal int Value
                {
                    get
                    {
                        int Local()
                        {
                            return 1;
                        }

                        System.Func<int> callback = () =>
                        {
                            return Local();
                        };
                        return callback();
                    }
                }
            }
            """;
        var findings = await NumericAnalyzerFixture.AnalyzeAsync(
            new ExecutableUnitCodeLineCountAnalyzer(), (Path, source));
        await Assert.That(findings).IsEmpty();
    }

    [Test]
    public async Task OversizedAccessorLocalFunctionAndLambdaEachReportAsync()
    {
        var source = CreateOversizedNestedUnits();
        var findings = await NumericAnalyzerFixture.AnalyzeAsync(
            new ExecutableUnitCodeLineCountAnalyzer(), (Path, source));
        await Assert.That(findings.Length).IsEqualTo(3);
        await Assert.That(findings.All(finding =>
            finding.Id == Id && finding.Severity == DiagnosticSeverity.Error)).IsTrue();
        var lines = findings.Select(static finding =>
            finding.Location.GetLineSpan().StartLinePosition.Line).ToArray();
        await Assert.That(lines.Contains(4)).IsTrue();
        await Assert.That(lines.Contains(6)).IsTrue();
        await Assert.That(lines.Contains(72)).IsTrue();
    }

    [Test]
    public async Task ConstructorsDestructorAndOperatorUnitsReportAsync()
    {
        var source = CreateOversizedNonMethodUnits();
        var findings = await NumericAnalyzerFixture.AnalyzeAsync(
            new ExecutableUnitCodeLineCountAnalyzer(), (Path, source));
        await Assert.That(findings.Length).IsEqualTo(5);
        await Assert.That(findings.All(finding =>
            finding.Id == Id && finding.Severity == DiagnosticSeverity.Error)).IsTrue();
    }

    [Test]
    public async Task AnonymousMethodAndEnclosingMethodBothIncludeItsSourceAsync()
    {
        var source = CreateOversizedAnonymousMethod();
        var findings = await NumericAnalyzerFixture.AnalyzeAsync(
            new ExecutableUnitCodeLineCountAnalyzer(), (Path, source));
        await Assert.That(findings.Length).IsEqualTo(2);
        await Assert.That(findings.All(finding =>
            finding.Id == Id && finding.Severity == DiagnosticSeverity.Error)).IsTrue();
        var lines = findings.Select(static finding =>
            finding.Location.GetLineSpan().StartLinePosition.Line).ToArray();
        await Assert.That(lines.Contains(2)).IsTrue();
        await Assert.That(lines.Contains(4)).IsTrue();
    }

    [Test]
    public async Task GeneratedExecutableUnitIsIgnoredByRoslynPolicyAsync()
    {
        var source = CreateMethod(65);
        var findings = await NumericAnalyzerFixture.AnalyzeAsync(
            new ExecutableUnitCodeLineCountAnalyzer(), ("/repo/src/Generated.g.cs", source));
        await Assert.That(findings).IsEmpty();
    }

    private static string CreateMethod(int lineCount)
    {
        var statements = Enumerable.Range(0, lineCount - 3)
            .Select(static index => $"        System.Console.WriteLine({index});");
        return "internal sealed class Subject\n{\n    internal static void Run()\n    {\n" +
            string.Join("\n", statements) + "\n    }\n}";
    }

    private static TextSpan MethodSpan(string source)
    {
        var start = source.IndexOf("internal static void Run()", StringComparison.Ordinal);
        var end = source.LastIndexOf("\n}", StringComparison.Ordinal);
        return TextSpan.FromBounds(start, end);
    }

    private static string CreateOversizedNestedUnits()
    {
        var localStatements = CreateStatements(62);
        var lambdaStatements = CreateStatements(62);
        return "internal sealed class Subject\n{\n    internal int Value\n    {\n        get\n" +
            "        {\n            int Local()\n            {\n" + localStatements +
            "\n                return 0;\n            }\n" +
            "            System.Func<int> callback = () =>\n            {\n" + lambdaStatements +
            "\n                return Local();\n            };\n            return callback();\n" +
            "        }\n    }\n}";
    }

    private static string CreateOversizedNonMethodUnits()
    {
        var body = CreateStatements(62, "        ");
        return "internal sealed class Subject\n{\n    public Subject()\n    {\n" + body +
            "\n    }\n    static Subject()\n    {\n" + body +
            "\n    }\n    ~Subject()\n    {\n" + body +
            "\n    }\n    public static Subject operator +(Subject left, Subject right)\n    {\n" + body +
            "\n        return left;\n    }\n    public static explicit operator int(Subject value)\n    {\n" +
            body + "\n        return 0;\n    }\n}";
    }

    private static string CreateOversizedAnonymousMethod()
    {
        var statements = CreateStatements(62);
        return "internal sealed class Subject\n{\n    internal static void Run()\n    {\n" +
            "        System.Func<int> callback = delegate\n        {\n" + statements +
            "\n            return 0;\n        };\n        callback();\n    }\n}";
    }

    private static string CreateStatements(int count, string indentation = "                ") => string.Join(
        "\n",
        Enumerable.Range(0, count).Select(index =>
            $"{indentation}System.Console.WriteLine({index});"));
}
