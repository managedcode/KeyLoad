using KeyLoad.UnitTests.CodeQuality.Helpers;

namespace KeyLoad.UnitTests.CodeQuality.Cases;

[NotInParallel]
internal sealed class CompiledDocumentBudgetTests
{
    [Test]
    public Task AcCq044PdbSeparatesOwnedAndGeneratedBudgetsWithoutLosingChecksumsOrHealthyBinding()
        => CompiledDocumentBudgetScenario.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
