namespace KeyLoad.AnalyzerTests.CodeQuality;

internal sealed class ObjectSwitchPatternTests
{
    [Test]
    public async Task SwitchCasesReportCapturedNativeObjectConversions()
    {
        const string source = """
            internal static class Subject
            {
                internal static int Read(object value) => value switch
                {
                    int number => number,
                    string text => text.Length,
                    _ => 0
                };
                internal static int ReadCase(object value)
                {
                    switch (value)
                    {
                        case decimal number: return (int)number;
                        default: return 0;
                    }
                }
            }
            """;
        var findings = await UntypedContractAnalyzerFixture.ConversionsAsync(source);
        await UntypedContractAnalyzerFixture.LocatedAsync(findings, UntypedContractAnalyzerFixture.ConversionId,
            source, "int number", "string text", "decimal number");
    }

    [Test]
    public async Task ACheckedSwitchSlotHasItsOwnReasonWhilePureTypeTestsStayUncaptured()
    {
        const string source = """
            internal static class Subject
            {
                internal static int Read(object value) => value switch
                {
                    int number /* KLD0042: This native slot was tagged as an integer before this exact typed extraction. */ => number,
                    string => 0,
                    _ => 0
                };
            }
            """;
        await Assert.That(await UntypedContractAnalyzerFixture.ConversionsAsync(source)).IsEmpty();
    }
}
