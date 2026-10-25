namespace KeyLoad.AnalyzerTests.CodeQuality;

internal sealed class VocabularyOwnershipTests
{
    [Test]
    [Arguments("/repo/tests/KeyLoad.FunctionalTests/Fixtures/Scope.cs")]
    [Arguments("/repo/tests/KeyLoad.FunctionalTests/Cases/Scope.cs")]
    public async Task ActualTestAttributesAndReachableSupportOwnResourceVocabulary(string path)
    {
        var word = RuntimePolicyAnalyzerFixture.Word("ResourceWord");
        var name = char.ToUpperInvariant(word[0]) + word[1..];
        var source = $$"""
            using TUnit.Core;
            internal class Checks
            {
                [Test] public void Run() { new {{name}}Scope().Read(); }
            }
            internal class {{name}}Scope { internal void Read() { } }
            """;
        var findings = await RuntimePolicyAnalyzerFixture.AnalyzeAsync(new UnauthorizedImplementationTermAnalyzer(), source, path);
        await Assert.That(findings).IsEmpty();
    }

    [Test]
    [Arguments("KeyLoad.ProcessRecoveryTests", "/repo/tests/KeyLoad.ProcessRecoveryTests/Program.cs")]
    [Arguments("KeyLoad.AppHost", "/repo/build/KeyLoad.AppHost/Program.cs")]
    [Arguments("KeyLoad.LoadTests.AppHost", "/repo/build/KeyLoad.LoadTests.AppHost/Program.cs")]
    public async Task RealProcessAndAspireEntryPointsOwnOnlyTheirReachableResources(string assembly, string path)
    {
        var word = RuntimePolicyAnalyzerFixture.Word("ResourceWord");
        var name = char.ToUpperInvariant(word[0]) + word[1..];
        var initialization = assembly == "KeyLoad.ProcessRecoveryTests" ? string.Empty
            : "var builder = Aspire.Hosting.DistributedApplication.CreateBuilder(System.Array.Empty<string>());";
        var source = $$"""
            {{initialization}}
            {{name}}Scope.Run();
            internal static class {{name}}Scope { internal static void Run() { } }
            """;
        var findings = await RuntimePolicyAnalyzerFixture.AnalyzeAsync(new UnauthorizedImplementationTermAnalyzer(),
            assembly, true, (path, source));
        await Assert.That(findings).IsEmpty();
    }

    [Test]
    public async Task RealTimeSeriesContractArraysAndGenericValuesPreserveDomainVocabulary()
    {
        var word = RuntimePolicyAnalyzerFixture.Word("DataWord");
        var recordName = typeof(KeyLoad.SampleRecord).FullName;
        var source = $$"""
            internal class Subject
            {
                internal {{recordName}}[] Read({{recordName}}[] values)
                {
                    const string description = "{{word}}";
                    return values;
                }
                internal System.Collections.Generic.List<{{recordName}}> ReadList(
                    System.Collections.Generic.List<{{recordName}}> values) => values;
            }
            """;
        var findings = await RuntimePolicyAnalyzerFixture.AnalyzeAsync(new UnauthorizedImplementationTermAnalyzer(), source);
        await Assert.That(findings).IsEmpty();
    }
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task RealDataSourceAndInitializerContractsOwnTheirSupport(bool dataSource)
    {
        var word = RuntimePolicyAnalyzerFixture.Word("ResourceWord");
        var name = char.ToUpperInvariant(word[0]) + word[1..] + "Scope";
        var source = dataSource
            ? $"[TUnit.Core.ClassDataSource<{name}>] internal class Checks {{ }} internal class {name} {{ public {name}() {{ }} }}"
            : $"internal class {name} : TUnit.Core.Interfaces.IAsyncInitializer {{ public System.Threading.Tasks.Task InitializeAsync() => System.Threading.Tasks.Task.CompletedTask; }}";
        var findings = await RuntimePolicyAnalyzerFixture.AnalyzeAsync(new UnauthorizedImplementationTermAnalyzer(), source,
            "/repo/tests/KeyLoad.FunctionalTests/Cases/Scope.cs");
        await Assert.That(findings).IsEmpty();
    }

    [Test]
    public async Task ActualBuilderTypedCallbacksRetainTheirAspireCompositionOwnership()
    {
        var word = RuntimePolicyAnalyzerFixture.Word("ResourceWord");
        var name = char.ToUpperInvariant(word[0]) + word[1..] + "Scope";
        var source = $$"""
            using Aspire.Hosting;
            var builder = DistributedApplication.CreateBuilder(System.Array.Empty<string>());
            {{name}}.Configure(builder);
            internal static class {{name}}
            {
                internal static T Configure<T>(T builder) where T : IDistributedApplicationBuilder => builder;
            }
            """;
        var findings = await RuntimePolicyAnalyzerFixture.AnalyzeAsync(new UnauthorizedImplementationTermAnalyzer(),
            "KeyLoad.AppHost", true, ("/repo/build/KeyLoad.AppHost/Program.cs", source));
        await Assert.That(findings).IsEmpty();
    }

    [Test]
    public async Task AnUnreachableClassDoesNotBorrowTheNearbyTestOwner()
    {
        var word = RuntimePolicyAnalyzerFixture.Word("ResourceWord");
        var name = char.ToUpperInvariant(word[0]) + word[1..] + "Scope";
        var source = $"internal class Checks {{ [TUnit.Core.Test] public void Run() {{ }} }} internal class {name} {{ }}";
        var findings = await RuntimePolicyAnalyzerFixture.AnalyzeAsync(new UnauthorizedImplementationTermAnalyzer(), source,
            "/repo/tests/KeyLoad.FunctionalTests/Cases/Scope.cs");
        await NumericAnalyzerFixture.AssertSingleFindingAsync(findings,
            UnauthorizedImplementationTermAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    }

}
