using KeyLoad.UnitTests.Features.CodeQuality.Helpers;

namespace KeyLoad.UnitTests.Features.CodeQuality.Cases;

internal sealed class NativePathMapCompilerTests
{
    [Test]
    public async Task AcCq044NativeCanonicalPdbBindsChecksumsRejectsChangedSourceAndAcceptsRestoration()
        => await NativePathMapScenario.CanonicalTamperAndFollowupAsync(TestContext.Current!.Execution.CancellationToken);

    [Test]
    public async Task AcCq044NativeUnknownPathMapIsRedactedAndCannotBindSource()
        => await NativePathMapScenario.UnknownRootAsync(TestContext.Current!.Execution.CancellationToken);

    [Test]
    public async Task AcCq044NativeEscapingPathMapRejectsWithoutLeakingDocumentPath()
        => await NativePathMapScenario.EscapingRootAsync(TestContext.Current!.Execution.CancellationToken);
}
