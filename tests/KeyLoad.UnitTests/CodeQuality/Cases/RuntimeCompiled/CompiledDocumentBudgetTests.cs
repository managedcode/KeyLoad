using KeyLoad.UnitTests.Features.CodeQuality.Helpers;

namespace KeyLoad.UnitTests.Features.CodeQuality.Cases;

[NotInParallel]
internal sealed class CompiledDocumentBudgetTests
{
    [Test]
    public Task AcCq044PdbSeparatesOwnedAndGeneratedBudgetsWithoutLosingChecksumsOrHealthyBinding()
        => CompiledDocumentBudgetScenario.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
