using KeyLoad.FunctionalTests.CodeQuality.Helpers;

namespace KeyLoad.FunctionalTests.CodeQuality.Cases;

[NotInParallel]
internal sealed class CompiledDocumentBudgetTests
{
    [Test]
    public Task AcCq044PdbSeparatesOwnedAndGeneratedBudgetsWithoutLosingChecksumsOrHealthyBinding()
        => CompiledDocumentBudgetScenario.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
