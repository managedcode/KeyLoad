using KeyLoad.UnitTests.Features.CodeQuality.Helpers;

namespace KeyLoad.UnitTests.Features.CodeQuality.Cases;

[NotInParallel]
internal sealed class NativeCompiledDocumentBudgetTests
{
    [Test]
    public Task AcCq044NativePdbSeparatesOwnedAndGeneratedBudgetsWithoutLosingChecksumsOrHealthyBinding()
        => NativeCompiledDocumentBudgetScenario.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
