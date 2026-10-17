using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-008: checks nested control-flow constructs at their exact boundary.</summary>
internal sealed class ControlFlowNestingAnalyzerTests
{
    private const string Id = "KLD0033";
    private const string Path = "/repo/src/Subject.cs";

    [Test]
    public async Task ControlFlowDepthTwoAndThreePassWhileFourReportsAsync()
    {
        foreach (var depth in new[] { 2, 3, 4 })
        {
            var source = CreateNestedIf(depth);
            var findings = await NumericAnalyzerFixture.AnalyzeAsync(
                new ControlFlowNestingAnalyzer(), (Path, source));
            if (depth <= 3)
            {
                await Assert.That(findings).IsEmpty();
            }
            else
            {
                await NumericAnalyzerFixture.AssertSingleFindingAsync(
                    findings, Id, DiagnosticSeverity.Error, Path, line: 2, column: 4,
                    span: MethodSpan(source));
            }
        }
    }

    [Test]
    public async Task TryCatchFinallyAreSiblingBranchesAndUsingDeclarationAddsNoDepthAsync()
    {
        const string source = """
            internal sealed class Subject
            {
                internal static void Run()
                {
                    using System.IO.MemoryStream stream = new();
                    try
                    {
                        if (stream.CanRead)
                        {
                            if (stream.CanSeek)
                            {
                                System.Console.WriteLine();
                            }
                        }
                    }
                    catch (System.Exception)
                    {
                        if (stream.CanWrite)
                        {
                            if (stream.CanSeek)
                            {
                                System.Console.WriteLine();
                            }
                        }
                    }
                    finally
                    {
                        if (stream.CanSeek)
                        {
                            if (stream.CanRead)
                            {
                                System.Console.WriteLine();
                            }
                        }
                    }
                }
            }
            """;
        var findings = await NumericAnalyzerFixture.AnalyzeAsync(
            new ControlFlowNestingAnalyzer(), (Path, source));
        await Assert.That(findings).IsEmpty();
    }

    [Test]
    public async Task GeneratedNestedControlFlowIsIgnoredByRoslynPolicyAsync()
    {
        var source = CreateNestedIf(4);
        var findings = await NumericAnalyzerFixture.AnalyzeAsync(
            new ControlFlowNestingAnalyzer(), ("/repo/src/Generated.g.cs", source));
        await Assert.That(findings).IsEmpty();
    }

    private static string CreateNestedIf(int depth)
    {
        var opening = string.Concat(Enumerable.Repeat("        if (true)\n        {\n", depth));
        var closing = string.Concat(Enumerable.Repeat("        }\n", depth));
        return "internal sealed class Subject\n{\n    internal static void Run()\n    {\n" +
            opening + "            System.Console.WriteLine();\n" + closing + "    }\n}";
    }

    private static TextSpan MethodSpan(string source)
    {
        var start = source.IndexOf("internal static void Run()", StringComparison.Ordinal);
        var end = source.LastIndexOf("\n}", StringComparison.Ordinal);
        return TextSpan.FromBounds(start, end);
    }
}
