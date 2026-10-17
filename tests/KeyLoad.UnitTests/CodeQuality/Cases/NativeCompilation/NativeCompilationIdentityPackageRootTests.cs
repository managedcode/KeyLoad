using KeyLoad.UnitTests.Features.CodeQuality.Helpers;

namespace KeyLoad.UnitTests.Features.CodeQuality.Cases;

internal sealed class NativeCompilationIdentityPackageRootTests
{
    [Test]
    public async Task AcCqPackageRootNativeMsbuildBindsTUnitAndRejectsCounterfeitImport()
        => await NativeCompilationIdentityPackageRootScenario.RunAsync(
            TestContext.Current!.Execution.CancellationToken);
}
