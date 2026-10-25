using Microsoft.CodeAnalysis.Diagnostics;

namespace KeyLoad.AnalyzerTests.CodeQuality;

internal sealed class AnalyzerInitializationTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task NullAnalysisContextIsRejectedBeforeRegistration(int index)
    {
        DiagnosticAnalyzer analyzer = index switch
        {
            0 => new UnauthorizedImplementationTermAnalyzer(),
            1 => new SynchronousZoneTreeAccessAnalyzer(),
            2 => new NullReturnAnalyzer(),
            _ => new ProgramCompositionRootAnalyzer()
        };
        var failure = Assert.ThrowsExactly<ArgumentNullException>(() => analyzer.Initialize(null!));
        await Assert.That(failure.ParamName).IsEqualTo("context");
    }
}
