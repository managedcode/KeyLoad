using KeyLoad.UnitTests.Features.CodeQuality.Helpers;

namespace KeyLoad.UnitTests.Features.CodeQuality.Cases;

internal sealed class CompilationIdentityPackageRootTests
{
    [Test]
    public async Task AcCqPackageRootMsbuildBindsTUnitAndRejectsCounterfeitImport()
        => await CompilationIdentityPackageRootScenario.RunAsync(
            TestContext.Current!.Execution.CancellationToken);
}
