using System.Text;

namespace KeyLoad.ComparisonHost.BenchmarkOrchestration;

internal sealed class ComparisonHostCaptureTests
{
    private const int ProgressCharacters = 100_000, MaximumRetainedCharacters = 32_768;
    private const char Progress = 'p';
    private const string TerminalFailure = "terminal execution failure";

    [Test]
    public async Task LongExecutionRetainsTerminalFailureWithinCaptureBound()
    {
        var output = new string(Progress, ProgressCharacters) + TerminalFailure;
        using var bytes = new MemoryStream(Encoding.UTF8.GetBytes(output));
        using var reader = new StreamReader(bytes);
        var retained = await ComparisonHostOutputCapture.CaptureAsync(reader, CancellationToken.None);
        await Assert.That(retained).IsEqualTo(output[^MaximumRetainedCharacters..]);
        await Assert.That(bytes.Position).IsEqualTo(bytes.Length);
    }
}
