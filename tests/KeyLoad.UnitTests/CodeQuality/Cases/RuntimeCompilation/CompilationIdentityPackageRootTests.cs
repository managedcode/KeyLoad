using KeyLoad.UnitTests.CodeQuality.Helpers;

namespace KeyLoad.UnitTests.CodeQuality.Cases;

internal sealed class CompilationIdentityPackageRootTests
{
    [Test]
    public async Task AcCqPackageRootMsbuildBindsTUnitAndRejectsCounterfeitImport()
        => await CompilationIdentityPackageRootScenario.RunAsync(
            TestContext.Current!.Execution.CancellationToken);
}
