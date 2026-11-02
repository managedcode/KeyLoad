namespace KeyLoad.AnalyzerTests.CodeQuality;

internal sealed class UntypedLocalDeclarationTests
{
    [Test]
    public async Task ExplicitAndInferredErasedLocalsHaveIndependentDeclarations()
    {
        const string source = """
            internal static class Subject
            {
                internal static void Run() { object first = null!; var second = (object)null!; }
            }
            """;
        var findings = await UntypedContractAnalyzerFixture.ContractsAsync(source);
        await UntypedContractAnalyzerFixture.LocatedAsync(findings, UntypedContractAnalyzerFixture.ContractId,
            source, "object", "var");
    }

    [Test]
    public async Task AnExactLocalReasonDoesNotAuthorizeTheFollowingDeclaration()
    {
        const string source = """
            internal static class Subject
            {
                internal static void Run()
                {
                    var /* KLD0041: This local retains the CLR value before its declared type is inspected. */ first = (object)null!;
                    var second = (object)null!;
                }
            }
            """;
        var findings = await UntypedContractAnalyzerFixture.ContractsAsync(source);
        await UntypedContractAnalyzerFixture.LocatedAsync(findings, UntypedContractAnalyzerFixture.ContractId,
            source, "var");
    }

    [Test]
    public async Task TypedLocalDeclarationsRemainTyped()
    {
        const string source = """
            internal static class Subject
            {
                internal static void Run() { string first = "text"; var second = first; }
            }
            """;
        var findings = await UntypedContractAnalyzerFixture.ContractsAsync(source);
        await Assert.That(findings).IsEmpty();
    }
}
