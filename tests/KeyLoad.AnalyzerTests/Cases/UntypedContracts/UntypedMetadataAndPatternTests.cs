namespace KeyLoad.AnalyzerTests.CodeQuality;

internal sealed class UntypedMetadataAndPatternTests
{
    [Test]
    public async Task NameofAndTypeofDoNotReadErasedRuntimeData()
    {
        const string source = """
            internal sealed class Subject
            {
                internal object /* KLD0041: The CLR member boundary is inspected independently at each access. */ Value
                    => throw new System.InvalidOperationException();
                internal void Run()
                {
                    const string name = nameof(Value);
                    var type = typeof(System.Threading.Tasks.Task<object>);
                }
            }
            """;
        var findings = await UntypedContractAnalyzerFixture.ContractsAsync(source);
        await Assert.That(findings).IsEmpty();
    }

    [Test]
    public async Task ExtractingATypedPatternValueFromObjectIsRuntimeConversion()
    {
        const string source = """
            internal static class Subject
            {
                internal static bool Read(object value)
                {
                    return value is int number;
                }
            }
            """;
        var findings = await UntypedContractAnalyzerFixture.ConversionsAsync(source);
        await UntypedContractAnalyzerFixture.LocatedAsync(findings, UntypedContractAnalyzerFixture.ConversionId,
            source, "value is int number");
    }

    [Test]
    public async Task ATypeOnlyPatternAndObjectIdentityPatternDoNotExtractAnotherType()
    {
        const string source = """
            internal static class Subject
            {
                internal static bool Read(object value) => value is int || value is object retained;
            }
            """;
        var findings = await UntypedContractAnalyzerFixture.ConversionsAsync(source);
        await Assert.That(findings).IsEmpty();
    }

    [Test]
    public async Task GenericPatternExtractionNeedsItsOwnReason()
    {
        const string source = """
            internal static class Subject
            {
                internal static bool Read<T>(object value) => value is T typed;
            }
            """;
        var findings = await UntypedContractAnalyzerFixture.ConversionsAsync(source);
        await UntypedContractAnalyzerFixture.LocatedAsync(findings, UntypedContractAnalyzerFixture.ConversionId,
            source, "value is T typed");
    }

    [Test]
    public async Task AnExactPatternReasonDoesNotAuthorizeTheFollowingPattern()
    {
        const string source = """
            internal static class Subject
            {
                internal static bool Read(object value)
                    => value is int first /* KLD0042: The CLR value is validated and extracted as this required numeric type. */
                        || value is long second;
            }
            """;
        var findings = await UntypedContractAnalyzerFixture.ConversionsAsync(source);
        await UntypedContractAnalyzerFixture.LocatedAsync(findings, UntypedContractAnalyzerFixture.ConversionId,
            source, "value is long second");
    }

    [Test]
    public async Task DynamicNumericConversionIsCheckedWithoutCallingItReferenceBoxing()
    {
        const string source = "internal static class Subject { internal static int Read(dynamic value) => (int)value; }";
        var findings = await UntypedContractAnalyzerFixture.ConversionsAsync(source);
        await UntypedContractAnalyzerFixture.LocatedAsync(findings, UntypedContractAnalyzerFixture.ConversionId,
            source, "(int)value");
    }
}
