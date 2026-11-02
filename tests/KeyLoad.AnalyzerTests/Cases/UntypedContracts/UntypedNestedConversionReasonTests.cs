namespace KeyLoad.AnalyzerTests.CodeQuality;

internal sealed class UntypedNestedConversionReasonTests
{
    [Test]
    public async Task AnOuterConversionReasonDoesNotAuthorizeAnInnerConversionWithTheSameLastToken()
    {
        const string source = """
            internal static class Subject
            {
                internal static void Run(object value)
                {
                    var retained = (object)(int)value /* KLD0042: Only the outer boxing is required by this CLR invocation boundary. */;
                }
            }
            """;
        var findings = await UntypedContractAnalyzerFixture.ConversionsAsync(source);
        await UntypedContractAnalyzerFixture.LocatedAsync(findings, UntypedContractAnalyzerFixture.ConversionId,
            source, "(int)value");
    }

    [Test]
    public async Task SeparateParenthesizedConversionsCanEachCarryAnExactReason()
    {
        const string source = """
            internal static class Subject
            {
                internal static void Run(object value)
                {
                    var retained = (object)((int)value /* KLD0042: This selected CLR value must be validated and unboxed as an integer. */) /* KLD0042: The receiving CLR invocation requires this explicit boxed integer value. */;
                }
            }
            """;
        var findings = await UntypedContractAnalyzerFixture.ConversionsAsync(source);
        await Assert.That(findings).IsEmpty();
    }
}
