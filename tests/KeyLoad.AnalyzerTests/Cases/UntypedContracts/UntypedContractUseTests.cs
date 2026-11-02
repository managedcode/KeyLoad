namespace KeyLoad.AnalyzerTests.CodeQuality;

internal sealed class UntypedContractUseTests
{
    [Test]
    public async Task AConstructedGenericInvocationIsCheckedUsingItsActualReturnType()
    {
        const string source = """
            internal static class Subject
            {
                private static T Read<T>() where T : new() => new T();
                internal static void Run() { var value = Read<object>(); }
            }
            """;
        var findings = await UntypedContractAnalyzerFixture.ContractsAsync(source);
        await UntypedContractAnalyzerFixture.LocatedAsync(findings, UntypedContractAnalyzerFixture.ContractId,
            source, "var", "Read<object>()");
    }

    [Test]
    public async Task EachPropertyAndFieldReadRequiresItsOwnReason()
    {
        const string source = """
            internal sealed class Subject
            {
                internal object /* KLD0041: The CLR member boundary is inspected independently at each access. */ Value => throw new System.InvalidOperationException();
                internal object /* KLD0041: The CLR member boundary is inspected independently at each access. */ item = null!;
                internal void Run() { var first = Value; var second = item; }
            }
            """;
        var findings = await UntypedContractAnalyzerFixture.ContractsAsync(source);
        await UntypedContractAnalyzerFixture.LocatedAsync(findings, UntypedContractAnalyzerFixture.ContractId,
            source, "var", "Value", "var", "item");
    }

    [Test]
    public async Task AnOuterInvocationReasonDoesNotAuthorizeTheInnerInvocation()
    {
        const string source = """
            internal abstract class Subject
            {
                internal abstract object /* KLD0041: The CLR member boundary is inspected independently at each access. */ Read();
                internal void Run()
                {
                    var value = System.Threading.Tasks.Task.FromResult(Read()) /* KLD0041: The task boundary deliberately retains the selected runtime metadata value. */;
                }
            }
            """;
        var findings = await UntypedContractAnalyzerFixture.ContractsAsync(source);
        await UntypedContractAnalyzerFixture.LocatedAsync(findings, UntypedContractAnalyzerFixture.ContractId,
            source, "var", "Read()");
    }

    [Test]
    public async Task ADeclarationReasonDoesNotAuthorizeItsCaller()
    {
        const string source = """
            internal abstract class Subject
            {
                internal abstract object /* KLD0041: The CLR member boundary is inspected independently at each access. */ Read();
                internal void Run() { var value = Read(); }
            }
            """;
        var findings = await UntypedContractAnalyzerFixture.ContractsAsync(source);
        await UntypedContractAnalyzerFixture.LocatedAsync(findings, UntypedContractAnalyzerFixture.ContractId,
            source, "var", "Read()");
    }

    [Test]
    public async Task ACommentOnTheWholeMethodDoesNotAuthorizeItsBodyOrReturnType()
    {
        const string source = """
            internal abstract class Subject
            {
                // KLD0041: This method-level comment cannot authorize its individual erased sites.
                internal object Run() { return Read(); }
                internal abstract object /* KLD0041: The CLR member boundary is inspected independently at each access. */ Read();
            }
            """;
        var findings = await UntypedContractAnalyzerFixture.ContractsAsync(source);
        await UntypedContractAnalyzerFixture.LocatedAsync(findings, UntypedContractAnalyzerFixture.ContractId,
            source, "object", "Read()");
    }

    [Test]
    public async Task AnExactCallReasonDoesNotAuthorizeTheFollowingCall()
    {
        const string source = """
            internal abstract class Subject
            {
                internal abstract object /* KLD0041: The CLR member boundary is inspected independently at each access. */ Read();
                internal void Run()
                {
                    var first = Read() /* KLD0041: This runtime metadata instance is checked at this exact read. */;
                    var second = Read();
                }
            }
            """;
        var findings = await UntypedContractAnalyzerFixture.ContractsAsync(source);
        await UntypedContractAnalyzerFixture.LocatedAsync(findings, UntypedContractAnalyzerFixture.ContractId,
            source, "var", "var", "Read()");
    }
}
