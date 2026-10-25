using Microsoft.CodeAnalysis;

namespace KeyLoad.AnalyzerTests.CodeQuality;

/// <summary>The type-size limit applies to the original database engine without a named exemption.</summary>
internal sealed class AggregateTypeAssemblyBoundaryTests
{
    private const string AssemblyName = "KeyLoad.Core";
    private const string SourcePath = "/repo/src/KeyLoad.Core/DatabaseEngine.cs";
    private const string TypeName = "DatabaseEngine";
    private const int MemberCount = 210;

    [Test]
    public async Task DatabaseEngineHasTheSameTypeSizeLimitAsEveryOtherType()
    {
        const string header = "namespace KeyLoad.Core;\ninternal sealed class DatabaseEngine\n{\n";
        const string footer = "\n}";
        var members = Enumerable.Range(0, MemberCount).Select(static index => $"    private int Value{index};");
        var source = header + string.Join('\n', members) + footer;
        var analyzer = new AggregateTypeCodeLineCountAnalyzer();
        var findings = await NumericAnalyzerFixture.AnalyzeSourcesAsync(analyzer, AssemblyName, (SourcePath, source));
        await NumericAnalyzerFixture.AssertSingleFindingAsync(findings, "KLD0031", DiagnosticSeverity.Error, SourcePath, line: 1);
        await Assert.That(findings[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture)).Contains(TypeName);
    }
}
