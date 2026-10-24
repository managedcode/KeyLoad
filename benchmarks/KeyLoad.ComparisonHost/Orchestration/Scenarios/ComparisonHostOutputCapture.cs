using System.Text;

namespace KeyLoad.ComparisonHost.BenchmarkOrchestration;

/// <summary>Retains a bounded prefix while joining the complete original redirected stream.</summary>
internal static class ComparisonHostOutputCapture
{
    private const int MaximumCapturedCharacters = 32_768;
    private const int CaptureChunkCharacters = 4_096;

    internal static async Task<string> CaptureAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var text = new StringBuilder();
        var buffer = new char[CaptureChunkCharacters];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            var remaining = MaximumCapturedCharacters - text.Length;
            if (remaining > 0)
            {
                text.Append(buffer, 0, Math.Min(count, remaining));
            }
        }
        return text.ToString();
    }
}
