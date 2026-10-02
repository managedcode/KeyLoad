using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Verifies bounded capture and the closed timeout receipt.</summary>
internal sealed class ComparisonHostStartupDiagnosticsTests
{
    private const string OutputCanary = "private-output-canary";
    private const string ArgumentCanary = "private-argument-canary";
    private const string EnvironmentCanary = "private-environment-canary";
    private const string ExceptionCanary = "private-exception-canary";
    private const string EndpointCanary = "https://private-endpoint.invalid/private-path";
    private const string OriginalTimeoutMessage = "The comparison host did not exit within its startup deadline.";
    private const string ExpectedTimeoutPrefix = OriginalTimeoutMessage + " Diagnostic: ";
    private const string ExitCodeProperty = "exitCode";
    private const string InvalidDimensionsProperty = "invalidDimensions";
    private const string MissingKeyLoadProperty = "missingKeyLoadSetting";
    private const string MissingQdrantProperty = "missingQdrantSetting";
    private const string ProcessExitStateProperty = "processExitState";
    private const string StageProperty = "stage";
    private const string StderrLengthProperty = "stderrLength";
    private const string StderrStateProperty = "stderrState";
    private const string StdoutLengthProperty = "stdoutLength";
    private const string StdoutStateProperty = "stdoutState";
    private static readonly string[] ExpectedReceiptPropertyNames =
    [
        ExitCodeProperty,
        InvalidDimensionsProperty,
        MissingKeyLoadProperty,
        MissingQdrantProperty,
        ProcessExitStateProperty,
        StageProperty,
        StderrLengthProperty,
        StderrStateProperty,
        StdoutLengthProperty,
        StdoutStateProperty
    ];

    [Test]
    public async Task AcTest009TimeoutReceiptContainsOnlyClosedFactsAndStaticMarkers()
    {
        var bytes = Encoding.UTF8.GetBytes(
            $"{{\"enrichedCanaryProperty\":\"{OutputCanary}\",\"arguments\":\"{ArgumentCanary}\","
            + $"\"environment\":\"{EnvironmentCanary}\",\"exception\":\"{ExceptionCanary}\","
            + $"\"endpoint\":\"{EndpointCanary}\"}} "
            + "Missing benchmark setting: Benchmarks:KeyLoadEndpoint");
        using var stream = new MemoryStream(bytes);
        using var reader = new StreamReader(stream);
        using var process = Process.GetCurrentProcess();
        var hasExited = process.HasExited;
        var output = new ComparisonHostOutputCapture();
        await output.CaptureAsync(reader, CancellationToken.None);
        var diagnostics = ComparisonHostStartupDiagnostics.Create(ComparisonHostStartupStage.ProcessExit,
            process, output.Snapshot(), ComparisonHostOutputCaptureSnapshot.Unavailable);

        var exception = ComparisonHostStartupDiagnostics.CreateTimeoutException(diagnostics);
        using var json = JsonDocument.Parse(exception.Message[ExpectedTimeoutPrefix.Length..]);
        var names = json.RootElement.EnumerateObject().Select(property => property.Name)
            .Order(StringComparer.Ordinal).ToArray();
        var expectedNames = ExpectedReceiptPropertyNames.Order(StringComparer.Ordinal).ToArray();

        await Assert.That(exception.Message.StartsWith(ExpectedTimeoutPrefix, StringComparison.Ordinal)).IsTrue();
        await Assert.That(Encoding.UTF8.GetByteCount(exception.Message))
            .IsLessThanOrEqualTo(ComparisonHostStartupDiagnostics.MaximumReceiptUtf8Bytes);
        await Assert.That(names.SequenceEqual(expectedNames)).IsTrue();
        await Assert.That(exception.Message.Contains(OutputCanary, StringComparison.Ordinal)).IsFalse();
        await Assert.That(exception.Message.Contains(ArgumentCanary, StringComparison.Ordinal)).IsFalse();
        await Assert.That(exception.Message.Contains(EnvironmentCanary, StringComparison.Ordinal)).IsFalse();
        await Assert.That(exception.Message.Contains(ExceptionCanary, StringComparison.Ordinal)).IsFalse();
        await Assert.That(exception.Message.Contains(EndpointCanary, StringComparison.Ordinal)).IsFalse();
        await Assert.That(json.RootElement.GetProperty(MissingKeyLoadProperty).GetBoolean()).IsTrue();
        await Assert.That(json.RootElement.GetProperty(StdoutLengthProperty).GetInt32()).IsEqualTo(bytes.Length);
        await Assert.That(json.RootElement.GetProperty(StageProperty).GetString()).IsEqualTo("ProcessExit");
        await Assert.That(json.RootElement.GetProperty(ProcessExitStateProperty).GetString())
            .IsEqualTo(hasExited ? "Exited" : "Running");
    }

    [Test]
    public async Task AcTest009UnknownReceiptEnumsAndLengthsProjectToUnavailable()
    {
        var diagnostics = new ComparisonHostStartupDiagnostics(
            (ComparisonHostStartupStage)int.MaxValue,
            (ComparisonHostProcessExitState)int.MinValue,
            17,
            new((ComparisonHostCaptureState)int.MaxValue, 0, true, true, true),
            new((ComparisonHostCaptureState)int.MinValue, 0, true, true, true));

        var exception = ComparisonHostStartupDiagnostics.CreateTimeoutException(diagnostics);
        using var json = JsonDocument.Parse(exception.Message[ExpectedTimeoutPrefix.Length..]);
        var receipt = json.RootElement;

        await Assert.That(receipt.GetProperty(StageProperty).GetString()).IsEqualTo("Unavailable");
        await Assert.That(receipt.GetProperty(ProcessExitStateProperty).GetString()).IsEqualTo("Unavailable");
        await Assert.That(receipt.GetProperty(ExitCodeProperty).ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(receipt.GetProperty(StdoutStateProperty).GetString()).IsEqualTo("Unavailable");
        await Assert.That(receipt.GetProperty(StdoutLengthProperty).GetInt32()).IsEqualTo(0);
        await Assert.That(receipt.GetProperty(StderrStateProperty).GetString()).IsEqualTo("Unavailable");
        await Assert.That(receipt.GetProperty(StderrLengthProperty).GetInt32()).IsEqualTo(0);
        await Assert.That(receipt.GetProperty(MissingKeyLoadProperty).GetBoolean()).IsFalse();
        await Assert.That(receipt.GetProperty(MissingQdrantProperty).GetBoolean()).IsFalse();
        await Assert.That(receipt.GetProperty(InvalidDimensionsProperty).GetBoolean()).IsFalse();
    }

    [Test]
    public async Task AcTest009OutOfRangeCaptureLengthsProjectToUnavailable()
    {
        var diagnostics = new ComparisonHostStartupDiagnostics(ComparisonHostStartupStage.OutputDrain,
            ComparisonHostProcessExitState.Exited, 17,
            new(ComparisonHostCaptureState.Completed, int.MaxValue, true, true, true),
            new(ComparisonHostCaptureState.Completed, -1, true, true, true));

        var exception = ComparisonHostStartupDiagnostics.CreateTimeoutException(diagnostics);
        using var json = JsonDocument.Parse(exception.Message[ExpectedTimeoutPrefix.Length..]);
        var receipt = json.RootElement;

        await Assert.That(receipt.GetProperty(StageProperty).GetString()).IsEqualTo("OutputDrain");
        await Assert.That(receipt.GetProperty(ProcessExitStateProperty).GetString()).IsEqualTo("Exited");
        await Assert.That(receipt.GetProperty(ExitCodeProperty).GetInt32()).IsEqualTo(17);
        await Assert.That(receipt.GetProperty(StdoutStateProperty).GetString()).IsEqualTo("Unavailable");
        await Assert.That(receipt.GetProperty(StdoutLengthProperty).GetInt32()).IsEqualTo(0);
        await Assert.That(receipt.GetProperty(StderrStateProperty).GetString()).IsEqualTo("Unavailable");
        await Assert.That(receipt.GetProperty(StderrLengthProperty).GetInt32()).IsEqualTo(0);
        await Assert.That(receipt.GetProperty(MissingKeyLoadProperty).GetBoolean()).IsFalse();
        await Assert.That(receipt.GetProperty(MissingQdrantProperty).GetBoolean()).IsFalse();
        await Assert.That(receipt.GetProperty(InvalidDimensionsProperty).GetBoolean()).IsFalse();
    }

    [Test]
    public async Task AcTest009RealStreamCaptureRetainsPrefixAndDrainsToEof()
    {
        var text = new string('x', ComparisonHostOutputCapture.MaximumCapturedCharacters)
            + OutputCanary + new string('y', ComparisonHostOutputCapture.CaptureChunkCharacters);
        var bytes = Encoding.UTF8.GetBytes(text);
        using var stream = new MemoryStream(bytes);
        using var reader = new StreamReader(stream);
        var output = new ComparisonHostOutputCapture();

        var captured = await output.CaptureAsync(reader, CancellationToken.None);
        var snapshot = output.Snapshot();

        await Assert.That(captured.Length).IsEqualTo(ComparisonHostOutputCapture.MaximumCapturedCharacters);
        await Assert.That(snapshot.Length).IsEqualTo(ComparisonHostOutputCapture.MaximumCapturedCharacters);
        await Assert.That(snapshot.IsCompleted).IsTrue();
        await Assert.That(stream.Position).IsEqualTo(stream.Length);
        await Assert.That(await reader.ReadAsync(new char[1].AsMemory(), CancellationToken.None)).IsEqualTo(0);
        await Assert.That(captured.Contains(OutputCanary, StringComparison.Ordinal)).IsFalse();
    }
}
