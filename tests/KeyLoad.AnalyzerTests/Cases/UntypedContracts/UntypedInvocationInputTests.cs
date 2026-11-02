namespace KeyLoad.AnalyzerTests.CodeQuality;

internal sealed class UntypedInvocationInputTests
{
    [Test]
    public async Task TypedReturnDoesNotHideAnErasedParameter()
    {
        const string source = """
            internal static class Subject
            {
                internal static int Consume(ManagedCode.Communication.Result<object> value) => 0;
                internal static void Run() { Consume(default); }
            }
            """;
        var findings = await UntypedContractAnalyzerFixture.ContractsAsync(source);
        await UntypedContractAnalyzerFixture.LocatedAsync(findings, UntypedContractAnalyzerFixture.ContractId,
            source, "ManagedCode.Communication.Result<object>", "Consume(default)");
    }

    [Test]
    public async Task AClrCollectionCallChecksItsErasedInputDespiteTypedAndVoidReturns()
    {
        const string source = """
            internal static class Subject
            {
                internal static void Run(System.Collections.IList values) { values.Add("text"); values.Clear(); }
            }
            """;
        var findings = await UntypedContractAnalyzerFixture.ContractsAsync(source);
        await UntypedContractAnalyzerFixture.LocatedAsync(findings, UntypedContractAnalyzerFixture.ContractId,
            source, "System.Collections.IList", "values.Add(\"text\")", "values.Clear()");
        await Assert.That(await UntypedContractAnalyzerFixture.ConversionsAsync(source)).IsEmpty();
    }

    [Test]
    public async Task AReducedExtensionCallChecksTheActualErasedReceiver()
    {
        const string source = """
            internal static class Extensions
            {
                internal static T Get<T>(this ManagedCode.Communication.Result<object> value) => default!;
            }
            internal static class Subject
            {
                internal static void Run(ManagedCode.Communication.Result<object> value) { value.Get<int>(); }
            }
            """;
        var findings = await UntypedContractAnalyzerFixture.ContractsAsync(source);
        await UntypedContractAnalyzerFixture.LocatedAsync(findings, UntypedContractAnalyzerFixture.ContractId,
            source, "ManagedCode.Communication.Result<object>", "ManagedCode.Communication.Result<object>",
            "value.Get<int>()");
    }

    [Test]
    public async Task ConstructedGenericParametersAreCheckedAtAVoidCall()
    {
        const string source = """
            internal static class Subject
            {
                internal static void Consume<T>(T value) { }
                internal static void Run() { Consume<object>(null!); }
            }
            """;
        var findings = await UntypedContractAnalyzerFixture.ContractsAsync(source);
        await UntypedContractAnalyzerFixture.LocatedAsync(findings, UntypedContractAnalyzerFixture.ContractId,
            source, "Consume<object>(null!)");
    }

    [Test]
    public async Task AnErasedReceiverIsCheckedWhenItsMethodReturnsAString()
    {
        const string source = """
            internal static class Subject
            {
                internal static void Run(object value) { value.ToString(); }
            }
            """;
        var findings = await UntypedContractAnalyzerFixture.ContractsAsync(source);
        await UntypedContractAnalyzerFixture.LocatedAsync(findings, UntypedContractAnalyzerFixture.ContractId,
            source, "object", "value.ToString()");
    }

    [Test]
    public async Task InheritedClrMethodsOnTypedReceiversRemainTyped()
    {
        const string source = """
            internal static class Subject
            {
                internal static void Run(string value) { value.ToString(); value.GetType(); }
            }
            """;
        var findings = await UntypedContractAnalyzerFixture.ContractsAsync(source);
        await Assert.That(findings).IsEmpty();
    }
}
