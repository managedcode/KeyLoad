using Microsoft.CodeAnalysis;

namespace KeyLoad.AnalyzerTests.CodeQuality;

internal sealed class ExternalNullReturnContractTests
{
    [Test]
    [Arguments("public object? GetService(System.Type serviceType) => null;")]
    [Arguments("object? System.IServiceProvider.GetService(System.Type serviceType) { return null; }")]
    public async Task NullableExternalInterfaceContractsArePreserved(string member)
    {
        var source = "#nullable enable\ninternal class Subject : System.IServiceProvider { " + member + " }";
        var findings = await AnalyzerFixture.AnalyzeAsync(new NullReturnAnalyzer(), source);
        await Assert.That(findings).IsEmpty();
    }

    [Test]
    public async Task NullableExternalOverridesArePreserved()
    {
        const string source = """
            #nullable enable
            internal class Subject : Microsoft.CodeAnalysis.CSharp.CSharpSyntaxRewriter
            {
                public override Microsoft.CodeAnalysis.SyntaxNode? Visit(Microsoft.CodeAnalysis.SyntaxNode? node) => null;
            }
            """;
        var findings = await AnalyzerFixture.AnalyzeAsync(new NullReturnAnalyzer(), source);
        await Assert.That(findings).IsEmpty();
    }

    [Test]
    public async Task ExternalGenericPropertyContractsArePreserved()
    {
        const string source = """
            #nullable enable
            internal class Subject : System.Collections.Generic.IEnumerator<string?>
            {
                public string? Current => null;
                object System.Collections.IEnumerator.Current => string.Empty;
                public bool MoveNext() => false;
                public void Reset() { }
                public void Dispose() { }
            }
            """;
        var findings = await AnalyzerFixture.AnalyzeAsync(new NullReturnAnalyzer(), source);
        await Assert.That(findings).IsEmpty();
    }

    [Test]
    public async Task CallbackBoundToAnExternalNullableApiContractIsPreserved()
    {
        const string source = """
            #nullable enable
            using System.Linq;
            internal class Subject
            {
                System.Collections.Generic.IEnumerable<string?> Read() => new[] { 1 }.Select<int, string?>(_ => null);
            }
            """;
        var findings = await AnalyzerFixture.AnalyzeAsync(new NullReturnAnalyzer(), source);
        await Assert.That(findings).IsEmpty();
    }

    [Test]
    public async Task AsynchronousExternalCallbackUsesItsPayloadContract()
    {
        const string source = """
            #nullable enable
            using System.Threading.Tasks;
            internal class Subject
            {
                Task<string?> Read() => Task.Run<string?>(async () => { await Task.Yield(); return null; });
            }
            """;
        var findings = await AnalyzerFixture.AnalyzeAsync(new NullReturnAnalyzer(), source);
        await Assert.That(findings).IsEmpty();
    }

    [Test]
    [Arguments("interface IContract { string? Read(); } internal class Subject : IContract { public string? Read() => null; }")]
    [Arguments("class Parent { public virtual string? Read() => string.Empty; } internal class Subject : Parent { public override string? Read() => null; }")]
    [Arguments("internal class Subject : System.IServiceProvider { public object? GetService(System.Type serviceType) => string.Empty; private string? Read() => null; }")]
    [Arguments("internal class Subject : System.IServiceProvider { public object? GetService(System.Type serviceType) { string? Read() => null; return Read(); } }")]
    [Arguments("internal class Subject { string? Read() => Call(() => null); string? Call(System.Func<string?> callback) => callback(); }")]
    [Arguments("internal class Subject : System.ICloneable { public object Clone() => null!; }")]
    public async Task LocalContractsAndNonNullableApisDoNotAuthorizeNull(string declarations)
    {
        var source = "#nullable enable\n" + declarations;
        var findings = await AnalyzerFixture.AnalyzeAsync(new NullReturnAnalyzer(), source);
        await NumericAnalyzerFixture.AssertSingleFindingAsync(findings, NullReturnAnalyzer.DiagnosticId, DiagnosticSeverity.Error);
    }
}
