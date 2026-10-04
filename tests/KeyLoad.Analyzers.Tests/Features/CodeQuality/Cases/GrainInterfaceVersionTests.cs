using Microsoft.CodeAnalysis;

namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-004: verifies grain interface version tests with real compiler fixtures.</summary>
internal sealed class GrainInterfaceVersionTests
{
    private const string Id = GrainInterfaceVersionAnalyzer.DiagnosticId;

    /// <summary>Verifies missing version reports located error.</summary>
    [Test]
    public async Task MissingVersionReportsLocatedErrorAsync()
    {
        const string source = """
            public interface IExampleGrain : Orleans.IGrainWithStringKey;
            """;
        var findings = await AnalyzerFixture.AnalyzeAsync(new GrainInterfaceVersionAnalyzer(), source);
        await AnalyzerFixture.AssertFindingAsync(findings, Id, DiagnosticSeverity.Error,
            source, "IExampleGrain");
    }

    /// <summary>Verifies zero version reports located error.</summary>
    [Test]
    public async Task ZeroVersionReportsLocatedErrorAsync()
    {
        const string source = """
            [Orleans.CodeGeneration.Version(0)]
            public interface IExampleGrain : Orleans.IGrainWithStringKey;
            """;
        var findings = await AnalyzerFixture.AnalyzeAsync(new GrainInterfaceVersionAnalyzer(), source);
        await AnalyzerFixture.AssertFindingAsync(findings, Id, DiagnosticSeverity.Error,
            source, "IExampleGrain");
    }

    /// <summary>Verifies positive version and plain interface are allowed.</summary>
    [Test]
    public async Task PositiveVersionAndPlainInterfaceAreAllowedAsync()
    {
        const string source = """
            [Orleans.CodeGeneration.Version(1)]
            public interface IExampleGrain : Orleans.IGrainWithStringKey;
            public interface IPlainContract;
            """;
        var findings = await AnalyzerFixture.AnalyzeAsync(new GrainInterfaceVersionAnalyzer(), source);
        await Assert.That(findings).IsEmpty();
    }

    /// <summary>Verifies external assembly is ignored.</summary>
    [Test]
    public async Task ExternalAssemblyIsIgnoredAsync()
    {
        const string source = """
            public interface IExternalGrain : Orleans.IGrainWithStringKey;
            """;
        var findings = await AnalyzerFixture.AnalyzeAsync(new GrainInterfaceVersionAnalyzer(), source,
            AnalyzerFixture.ExternalAssembly);
        await Assert.That(findings).IsEmpty();
    }
}
