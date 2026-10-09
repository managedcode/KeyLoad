using Microsoft.CodeAnalysis;

namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-004: verifies orleans contract constructor tests with real compiler fixtures.</summary>
internal sealed class OrleansContractConstructorTests
{
    private const string Id = OrleansContractConstructorAnalyzer.DiagnosticId;

    /// <summary>Allows native exception constructors with actual framework base serialization.</summary>
    [Test]
    public async Task SerializedNativeExceptionConstructorsAreAllowedAsync()
    {
        const string source = """
            [Orleans.GenerateSerializer]
            public sealed class NativeFailure : global::System.Exception
            {
                public NativeFailure() : base("safe failure") { }
                public NativeFailure(string message) : base(message) { }
                public NativeFailure(string message, global::System.Exception cause) : base(message, cause) { }
                public NativeFailure(int code, string message) : this(code, message, null) { }
                private NativeFailure(int code, string message, global::System.Exception cause)
                    : base(message, cause) => Code = code;
                [Orleans.Id(0)] public int Code { get; }
            }
            """;
        var findings = await AnalyzerFixture.AnalyzeAsync(new OrleansContractConstructorAnalyzer(),
            source, AnalyzerFixture.CoreAssembly);
        await Assert.That(findings).IsEmpty();
    }

    /// <summary>Allows indirect native exception inheritance in the shared contract path.</summary>
    [Test]
    public async Task SerializedIndirectNativeExceptionConstructorsAreAllowedAsync()
    {
        const string source = """
            public class NativeFailureBase : global::System.Exception
            {
                protected NativeFailureBase(string message, global::System.Exception cause) : base(message, cause) { }
            }
            [Orleans.GenerateSerializer]
            public sealed class NativeFailure : NativeFailureBase
            {
                public NativeFailure(string message, global::System.Exception cause) : base(message, cause) { }
            }
            """;
        var findings = await AnalyzerFixture.AnalyzeAsync(new OrleansContractConstructorAnalyzer(), source);
        await Assert.That(findings).IsEmpty();
    }

    /// <summary>A same-named serialized DTO remains an error without native Exception inheritance.</summary>
    [Test]
    public async Task ExceptionNameLookalikeDtoConstructorReportsLocatedErrorAsync()
    {
        const string source = """
            namespace Example
            {
                [Orleans.GenerateSerializer]
                public sealed class Exception
                {
                    public Exception(string message) => Message = message;
                    [Orleans.Id(0)] public string Message { get; }
                }
            }
            """;
        var findings = await AnalyzerFixture.AnalyzeAsync(new OrleansContractConstructorAnalyzer(),
            source, AnalyzerFixture.CoreAssembly);
        await AnalyzerFixture.AssertFindingAsync(findings, Id, DiagnosticSeverity.Error,
            source, "public Exception");
    }

    /// <summary>Verifies serialized dto constructor reports located error.</summary>
    [Test]
    public async Task SerializedDtoConstructorReportsLocatedErrorAsync()
    {
        const string source = """
            [Orleans.GenerateSerializer]
            public sealed class Payload
            {
                public Payload(int value) => Value = value;
                [Orleans.Id(0)] public int Value { get; set; }
            }
            """;
        var findings = await AnalyzerFixture.AnalyzeAsync(new OrleansContractConstructorAnalyzer(),
            source, AnalyzerFixture.CoreAssembly);
        await AnalyzerFixture.AssertFindingAsync(findings, Id, DiagnosticSeverity.Error,
            source, "public Payload");
    }

    /// <summary>Verifies shared contract folder constructor reports located error.</summary>
    [Test]
    public async Task SharedContractFolderConstructorReportsLocatedErrorAsync()
    {
        const string source = """
            public sealed class ContractOptions
            {
                public ContractOptions() { }
            }
            """;
        var findings = await AnalyzerFixture.AnalyzeAsync(new OrleansContractConstructorAnalyzer(),
            source);
        await AnalyzerFixture.AssertFindingAsync(findings, Id, DiagnosticSeverity.Error,
            source, "public ContractOptions");
    }

    /// <summary>Verifies static factory and helper outside contracts are allowed.</summary>
    [Test]
    public async Task StaticFactoryAndHelperOutsideContractsAreAllowedAsync()
    {
        const string source = """
            [Orleans.GenerateSerializer]
            public sealed class Payload
            {
                [Orleans.Id(0)] public int Value { get; set; }
                public static Payload Create(int value) => new() { Value = value };
            }
            public sealed class RouteSelector
            {
                public RouteSelector(string route) => Route = route;
                public string Route { get; }
            }
            """;
        var findings = await AnalyzerFixture.AnalyzeAsync(new OrleansContractConstructorAnalyzer(),
            source, path: AnalyzerFixture.OrleansModelsPath);
        await Assert.That(findings).IsEmpty();
    }

    /// <summary>Verifies external test and generated sources are ignored.</summary>
    [Test]
    public async Task ExternalTestAndGeneratedSourcesAreIgnoredAsync()
    {
        const string source = """
            [Orleans.GenerateSerializer]
            public sealed class Payload
            {
                public Payload() { }
            }
            """;
        var external = await AnalyzerFixture.AnalyzeAsync(new OrleansContractConstructorAnalyzer(),
            source, AnalyzerFixture.ExternalAssembly);
        var unitTests = await AnalyzerFixture.AnalyzeAsync(new OrleansContractConstructorAnalyzer(),
            source, AnalyzerFixture.UnitTestAssembly);
        var generated = await AnalyzerFixture.AnalyzeAsync(new OrleansContractConstructorAnalyzer(),
            "// <auto-generated/>\n" + source, path: AnalyzerFixture.GeneratedPath);
        await Assert.That(external).IsEmpty();
        await Assert.That(unitTests).IsEmpty();
        await Assert.That(generated).IsEmpty();
    }
}
