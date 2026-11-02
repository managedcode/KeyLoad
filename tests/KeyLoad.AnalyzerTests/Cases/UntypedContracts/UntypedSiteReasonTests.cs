namespace KeyLoad.AnalyzerTests.CodeQuality;

internal sealed class UntypedSiteReasonTests
{
    [Test]
    [Arguments("")]
    [Arguments(" ")]
    [Arguments("TODO")]
    [Arguments("....................")]
    public async Task MissingOrPlaceholderReasonsDoNotAuthorizeADeclaration(string reason)
    {
        var source = "internal abstract class Subject { internal abstract object /* KLD0041: " + reason + " */ Read(); }";
        var findings = await UntypedContractAnalyzerFixture.ContractsAsync(source);
        await UntypedContractAnalyzerFixture.LocatedAsync(findings, UntypedContractAnalyzerFixture.ContractId,
            source, "object");
    }

    [Test]
    public async Task RequiredExternalClrContractNeedsAnExactDeclarationAndCallReason()
    {
        const string source = """
            internal sealed class Subject : System.ICloneable
            {
                public object /* KLD0041: ICloneable fixes this return type; callers inspect the concrete clone type. */ Clone()
                    => MemberwiseClone() /* KLD0041: Object.MemberwiseClone is the CLR shallow-clone boundary required here. */;
            }
            """;
        var findings = await UntypedContractAnalyzerFixture.ContractsAsync(source);
        await Assert.That(findings).IsEmpty();
    }

    [Test]
    public async Task AnotherRuleMarkerCannotAuthorizeAnErasedReturn()
    {
        const string source = """
            internal abstract class Subject
            {
                internal abstract object /* KLD0042: The conversion explanation does not authorize a declaration. */ Read();
            }
            """;
        var findings = await UntypedContractAnalyzerFixture.ContractsAsync(source);
        await UntypedContractAnalyzerFixture.LocatedAsync(findings, UntypedContractAnalyzerFixture.ContractId,
            source, "object");
    }
}
