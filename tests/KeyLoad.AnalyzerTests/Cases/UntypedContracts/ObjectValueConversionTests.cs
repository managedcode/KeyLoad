namespace KeyLoad.AnalyzerTests.CodeQuality;

internal sealed class ObjectValueConversionTests
{
    [Test]
    public async Task ValueBoxingAndUnboxingHaveIndependentLocatedWarnings()
    {
        const string source = """
            internal static class Subject
            {
                internal static void Run()
                {
                    var value = (object)42;
                    var number = (int)value;
                }
            }
            """;
        var findings = await UntypedContractAnalyzerFixture.ConversionsAsync(source);
        await UntypedContractAnalyzerFixture.LocatedAsync(findings, UntypedContractAnalyzerFixture.ConversionId,
            source, "(object)42", "(int)value");
    }

    [Test]
    public async Task NullableAndUnconstrainedGenericValuesCanBox()
    {
        const string source = """
            internal static class Subject
            {
                internal static object Number() => (int?)42;
                internal static object Generic<T>(T value) => value;
            }
            """;
        var findings = await UntypedContractAnalyzerFixture.ConversionsAsync(source);
        await UntypedContractAnalyzerFixture.LocatedAsync(findings, UntypedContractAnalyzerFixture.ConversionId,
            source, "(int?)42", "value");
    }

    [Test]
    public async Task ReferenceUpcastsAndIdentityCastsAreNotReportedAsBoxing()
    {
        const string source = """
            internal static class Subject
            {
                internal static void Run(string value)
                {
                    var erased = (object)value;
                    var same = (object)erased;
                }
            }
            """;
        var findings = await UntypedContractAnalyzerFixture.ConversionsAsync(source);
        await Assert.That(findings).IsEmpty();
    }

    [Test]
    public async Task ReferenceConstrainedGenericsAndInterfaceUpcastsAreNotBoxing()
    {
        const string source = """
            internal static class Subject
            {
                internal static object Keep<T>(T value) where T : class => value;
                internal static System.ICloneable Keep(string value) => value;
            }
            """;
        var findings = await UntypedContractAnalyzerFixture.ConversionsAsync(source);
        await Assert.That(findings).IsEmpty();
    }

    [Test]
    public async Task AReferenceCastFromObjectIsDescribedAsRuntimeConversion()
    {
        const string source = "internal static class Subject { internal static string Read(object value) => (string)value; }";
        var findings = await UntypedContractAnalyzerFixture.ConversionsAsync(source);
        await UntypedContractAnalyzerFixture.LocatedAsync(findings, UntypedContractAnalyzerFixture.ConversionId,
            source, "(string)value");
        await Assert.That(findings[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("runtime object conversion", StringComparison.Ordinal)).IsTrue();
        await Assert.That(findings[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("boxing", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task ACallReasonDoesNotAuthorizeItsSeparateBoxingConversion()
    {
        const string source = """
            internal static class Subject
            {
                internal static int Read() => 42;
                internal static object Run() => Read() /* KLD0041: The returned metadata is consumed only by this CLR boundary. */;
            }
            """;
        var findings = await UntypedContractAnalyzerFixture.ConversionsAsync(source);
        await UntypedContractAnalyzerFixture.LocatedAsync(findings, UntypedContractAnalyzerFixture.ConversionId,
            source, "Read()");
    }

    [Test]
    public async Task AnExactConversionReasonAuthorizesOnlyThatConversion()
    {
        const string source = """
            internal static class Subject
            {
                internal static void Run()
                {
                    var first = (object)42 /* KLD0042: The framework formatter requires a boxed numeric message argument here. */;
                    var second = (object)43;
                }
            }
            """;
        var findings = await UntypedContractAnalyzerFixture.ConversionsAsync(source);
        await UntypedContractAnalyzerFixture.LocatedAsync(findings, UntypedContractAnalyzerFixture.ConversionId,
            source, "(object)43");
    }
}
