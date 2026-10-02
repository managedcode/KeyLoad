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
    public async Task NestedLambdaStartsAtItsOwnDepthAsync()
    {
        const string source = """
            internal sealed class Subject
            {
                internal static void Run()
                {
                    if (true)
                    {
                        System.Action callback = () =>
                        {
                            if (true)
                            {
                                if (true)
                                {
                                    if (true)
                                    {
                                        if (true)
                                        {
                                            System.Console.WriteLine();
                                        }
                                    }
                                }
                            }
                        };
                        callback();
                    }
                }
            }
            """;
        var findings = await NumericAnalyzerFixture.AnalyzeAsync(
            new ControlFlowNestingAnalyzer(), (Path, source));
        await NumericAnalyzerFixture.AssertSingleFindingAsync(
            findings, Id, DiagnosticSeverity.Error, Path, line: 6);
    }

    [Test]
    public async Task NestedLocalFunctionStartsAtItsOwnDepthAsync()
    {
        const string source = """
            internal sealed class Subject
            {
                internal static void Run()
                {
                    void Local()
                    {
                        if (true)
                        {
                            if (true)
                            {
                                if (true)
                                {
                                    if (true)
                                    {
                                        System.Console.WriteLine();
                                    }
                                }
                            }
                        }
                    }
                    Local();
                }
            }
            """;
        var findings = await NumericAnalyzerFixture.AnalyzeAsync(
            new ControlFlowNestingAnalyzer(), (Path, source));
        await NumericAnalyzerFixture.AssertSingleFindingAsync(
            findings, Id, DiagnosticSeverity.Error, Path, line: 4);
    }

    [Test]
    public async Task LoopsSwitchesAndConditionalExpressionsContributeToDepthAsync()
    {
        const string source = """
            internal sealed class Subject
            {
                internal static void Run()
                {
                    for (var index = 0; index < 1; index++)
                    {
                        lock (new object())
                        {
                            switch (index)
                            {
                                case 0:
                                    _ = index switch
                                    {
                                        0 => index == 0 ? 1 : 2,
                                        _ => 0
                                    };
                                    break;
                            }
                        }
                    }
                }
            }
            """;
        var findings = await NumericAnalyzerFixture.AnalyzeAsync(
            new ControlFlowNestingAnalyzer(), (Path, source));
        await NumericAnalyzerFixture.AssertSingleFindingAsync(
            findings, Id, DiagnosticSeverity.Error, Path, line: 2);
    }

    [Test]
    public async Task GeneratedNestedControlFlowIsIgnoredByRoslynPolicyAsync()
    {
        var source = CreateNestedIf(4);
        var findings = await NumericAnalyzerFixture.AnalyzeAsync(
            new ControlFlowNestingAnalyzer(), ("/repo/src/Generated.g.cs", source));
        await Assert.That(findings).IsEmpty();
    }

    [Test]
    public async Task ElseIfEveryLoopUsingStatementAndFixedAddNestingAsync()
    {
        const string source = """
            internal sealed class Subject
            {
                internal static void ElseIf(bool a, bool b, bool c, bool d)
                {
                    if (a) { }
                    else if (b) { }
                    else if (c) { }
                    else if (d) { }
                }

                internal static void Foreach(int[] values)
                {
                    if (true) if (true) if (true) foreach (var value in values) { }
                }

                internal static void Do()
                {
                    if (true) if (true) if (true) do { } while (false);
                }

                internal static void Using()
                {
                    if (true) if (true) if (true) using (var stream = new System.IO.MemoryStream()) { }
                }

                internal static unsafe void Fixed()
                {
                    if (true) if (true) if (true) fixed (char* pointer = "text") { }
                }
            }
            """;
        var findings = await NumericAnalyzerFixture.AnalyzeWithUnsafeAsync(
            new ControlFlowNestingAnalyzer(), (Path, source));
        await Assert.That(findings.Length).IsEqualTo(5);
        await Assert.That(findings.All(finding =>
            finding.Id == Id && finding.Severity == DiagnosticSeverity.Error &&
            finding.Location.SourceTree?.FilePath == Path)).IsTrue();
        var starts = findings.Select(static finding =>
                finding.Location.SourceSpan.Start)
            .Order()
            .ToArray();
        var expectedStarts = new[] { "internal static void ElseIf", "internal static void Foreach",
                "internal static void Do()", "internal static void Using()",
                "internal static unsafe void Fixed()" }
            .Select(signature => source.IndexOf(signature, StringComparison.Ordinal))
            .Order()
            .ToArray();
        await Assert.That(starts.SequenceEqual(expectedStarts)).IsTrue();
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
        var start = source.IndexOf("    internal static void Run()", StringComparison.Ordinal);
        var end = source.LastIndexOf("\n}", StringComparison.Ordinal);
        return TextSpan.FromBounds(start, end);
    }
}
