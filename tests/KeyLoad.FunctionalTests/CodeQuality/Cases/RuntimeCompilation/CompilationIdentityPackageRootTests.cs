using KeyLoad.FunctionalTests.CodeQuality.Helpers;

namespace KeyLoad.FunctionalTests.CodeQuality.Cases;

internal sealed class CompilationIdentityPackageRootTests
{
    [Test]
    public async Task AcCqPackageRootMsbuildBindsTUnitAndRejectsCounterfeitImport()
        => await CompilationIdentityPackageRootScenario.RunAsync(
            TestContext.Current!.Execution.CancellationToken);
}
