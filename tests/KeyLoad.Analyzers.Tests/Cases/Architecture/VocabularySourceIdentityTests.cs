using Microsoft.CodeAnalysis;

namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

internal sealed class VocabularySourceIdentityTests
{
    [Test]
    public async Task ActualCanonicalVocabularySourcesRetainTheirExactDefinitionOwnership()
    {
        var root = SiteAnalyzerCoverageXmlBuilder.FindRepositoryRoot();
        var paths = new[]
        {
            "analyzers/KeyLoad.Analyzers/Contracts/ImplementationTermCatalog.cs",
            "analyzers/KeyLoad.Analyzers/Analysis/ImplementationTerms/ImplementationTermReader.cs"
        };
        var sources = new List<(string Path, string Text)>();
        foreach (var path in paths)
        {
            sources.Add((Path.Combine(root, path), await File.ReadAllTextAsync(Path.Combine(root, path))));
        }
        var findings = await RuntimePolicyAnalyzerFixture.AnalyzeAsync(new UnauthorizedImplementationTermAnalyzer(),
            "KeyLoad.Analyzers", false, sources.ToArray());
        await Assert.That(findings).IsEmpty();
    }

    [Test]
    [Arguments(64, true, true)]
    [Arguments(63, true, false)]
    [Arguments(65, true, false)]
    [Arguments(64, false, false)]
    public async Task SourceMetadataRequiresAnExactHashAndAResolvedTestOwner(int length, bool sourceExists, bool accepted)
    {
        var word = RuntimePolicyAnalyzerFixture.Word("ResourceWord");
        var name = char.ToUpperInvariant(word[0]) + word[1..] + "Scope";
        var relative = "tests/KeyLoad.UnitTests/Cases/" + name + ".cs";
        var metadata = $"[assembly: System.Reflection.AssemblyMetadata(\"KeyLoad.FunctionalCompileIdentity.Source\", \"{relative}|{new string('a', length)}\")]";
        var owner = $"using TUnit.Core; internal class {name} {{ [Test] public void Run() {{ }} }}";
        var sources = new List<(string Path, string Text)> { ("/repo/build/Identity.g.cs", metadata) };
        if (sourceExists)
        {
            sources.Add(("/repo/" + relative, owner));
        }
        var findings = await RuntimePolicyAnalyzerFixture.AnalyzeAsync(new UnauthorizedImplementationTermAnalyzer(),
            "KeyLoad.UnitTests", false, sources.ToArray());
        if (accepted)
        {
            await Assert.That(findings).IsEmpty();
        }
        else
        {
            await NumericAnalyzerFixture.AssertSingleFindingAsync(findings,
                UnauthorizedImplementationTermAnalyzer.DiagnosticId, DiagnosticSeverity.Error);
        }
    }
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task SourceMetadataResolvesBothTheTimeSeriesPathAndTheActualDomainNamespace(bool featurePath)
    {
        var word = RuntimePolicyAnalyzerFixture.Word("DataWord");
        var relative = "src/KeyLoad/" + (featurePath ? "TimeSeries/" : "Policies/") + word + ".cs";
        var metadata = $"[assembly: System.Reflection.AssemblyMetadata(\"KeyLoad.FunctionalCompileIdentity.Source\", \"{relative}|{new string('a', 64)}\")]";
        const string owner = "namespace KeyLoad.Core.Features.TimeSeries { internal class Subject { } }";
        var findings = await RuntimePolicyAnalyzerFixture.AnalyzeAsync(new UnauthorizedImplementationTermAnalyzer(),
            "KeyLoad.PolicyChecks", false, ("/repo/build/Identity.g.cs", metadata), ("/repo/" + relative, owner));
        await Assert.That(findings).IsEmpty();
    }

}
