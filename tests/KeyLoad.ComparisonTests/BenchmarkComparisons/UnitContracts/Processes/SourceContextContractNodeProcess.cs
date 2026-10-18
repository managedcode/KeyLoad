using System.Text;
using System.Text.Json;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Uses the existing bounded, joined native Node lifetime for source-contract inputs.</summary>
internal static class SourceContextContractNodeProcess
{
    private const int MaximumInputBytes = 64 * 1024;
    private const string InputLimit = "The source contract input exceeded its bound.";
    private const string InvalidOutput = "The source contract probe failed.";
    private const string Succeeded = "ok";
    private const string Value = "value";
    private const string Message = "message";

    internal static async Task<SourceContextContractNodeResult> RunAsync(object request, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(request);
        if (bytes.Length > MaximumInputBytes)
        {
            throw new InvalidOperationException(InputLimit);
        }
        var result = await SessionNodeProcess.RunAsync(["--input-type=module", "-e", SourceContextContractNodeProgram.Source,
            SessionNodeProcess.Module("source-context.mjs"), Convert.ToBase64String(bytes)], token);
        if (result.ExitCode != 0 || result.Error.Length != 0 || Encoding.UTF8.GetByteCount(result.Output) > MaximumInputBytes)
        {
            throw new InvalidOperationException(InvalidOutput);
        }
        using var document = JsonDocument.Parse(result.Output);
        var root = document.RootElement;
        var success = root.GetProperty(Succeeded).GetBoolean();
        return new(success, success ? root.GetProperty(Value).Clone() : default,
            success ? string.Empty : root.GetProperty(Message).GetString() ?? string.Empty);
    }
}
