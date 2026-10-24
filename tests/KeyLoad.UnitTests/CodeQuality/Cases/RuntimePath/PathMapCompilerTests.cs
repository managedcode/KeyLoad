using KeyLoad.UnitTests.CodeQuality.Helpers;

namespace KeyLoad.UnitTests.CodeQuality.Cases;

internal sealed class PathMapCompilerTests
{
    [Test]
    public async Task AcCq044CanonicalPdbBindsChecksumsRejectsChangedSourceAndAcceptsRestoration()
        => await PathMapScenario.CanonicalTamperAndFollowupAsync(TestContext.Current!.Execution.CancellationToken);

    [Test]
    public async Task AcCq044UnknownPathMapIsRedactedAndCannotBindSource()
        => await PathMapScenario.UnknownRootAsync(TestContext.Current!.Execution.CancellationToken);

    [Test]
    public async Task AcCq044EscapingPathMapRejectsWithoutLeakingDocumentPath()
        => await PathMapScenario.EscapingRootAsync(TestContext.Current!.Execution.CancellationToken);
}
