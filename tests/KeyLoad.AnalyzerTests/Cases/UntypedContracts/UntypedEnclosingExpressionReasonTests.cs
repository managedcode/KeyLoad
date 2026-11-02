namespace KeyLoad.AnalyzerTests.CodeQuality;

internal sealed class UntypedEnclosingExpressionReasonTests
{
    [Test]
    public async Task ConditionalAccessReasonCoversItsMemberWithoutAuthorizingAnOuterConditional()
    {
        const string source = """
            internal sealed class Subject
            {
                internal object /* KLD0041: This persisted slot contains the business contract selected by its operation kind. */ Value { get; } = string.Empty;
                internal void Run(Subject? owner, bool choose)
                {
                    _ = (owner?.Value /* KLD0041: This individual nullable member read preserves the native stored payload before typed validation. */);
                    _ = owner?.Value;
                    _ = choose ? owner?.Value : owner?.Value /* KLD0041: This outer conditional does not justify either individual member read. */;
                }
            }
            """;
        var findings = await UntypedContractAnalyzerFixture.ContractsAsync(source);
        await UntypedContractAnalyzerFixture.LocatedAsync(findings, UntypedContractAnalyzerFixture.ContractId,
            source, ".Value", ".Value", ".Value");
    }

    [Test]
    public async Task AConditionalReasonDoesNotAuthorizeItsLastInnerInvocation()
    {
        const string source = """
            internal abstract class Subject
            {
                internal abstract object /* KLD0041: The CLR member boundary is inspected independently at each access. */ Read();
                internal void Run(bool choose)
                {
                    _ = choose ? Read() : Read() /* KLD0041: This outer expression does not explain either individual metadata read. */;
                }
            }
            """;
        var findings = await UntypedContractAnalyzerFixture.ContractsAsync(source);
        await UntypedContractAnalyzerFixture.LocatedAsync(findings, UntypedContractAnalyzerFixture.ContractId,
            source, "Read()", "Read()");
    }

    [Test]
    public async Task AnAwaitReasonDoesNotAuthorizeItsInnerInvocation()
    {
        const string source = """
            internal abstract class Subject
            {
                internal abstract System.Threading.Tasks.Task<object> /* KLD0041: The CLR task boundary is inspected independently at each access. */ ReadAsync();
                internal async System.Threading.Tasks.Task Run()
                {
                    _ = await ReadAsync() /* KLD0041: The await expression cannot authorize the individual metadata task call. */;
                }
            }
            """;
        var findings = await UntypedContractAnalyzerFixture.ContractsAsync(source);
        await UntypedContractAnalyzerFixture.LocatedAsync(findings, UntypedContractAnalyzerFixture.ContractId,
            source, "ReadAsync()");
    }

    [Test]
    public async Task ParenthesizedInnerInvocationsCanCarryTheirOwnReasons()
    {
        const string source = """
            internal abstract class Subject
            {
                internal abstract System.Threading.Tasks.Task<object> /* KLD0041: The CLR task boundary is inspected independently at each access. */ ReadAsync();
                internal async System.Threading.Tasks.Task Run(bool choose)
                {
                    _ = await (ReadAsync() /* KLD0041: This individual task read retains the CLR metadata until its type is inspected. */);
                    _ = choose
                        ? (ReadAsync() /* KLD0041: The selected first task read retains the CLR metadata until its type is inspected. */)
                        : (ReadAsync() /* KLD0041: The selected second task read retains the CLR metadata until its type is inspected. */);
                }
            }
            """;
        var findings = await UntypedContractAnalyzerFixture.ContractsAsync(source);
        await Assert.That(findings).IsEmpty();
    }
}
