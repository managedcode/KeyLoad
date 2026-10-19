using System.Text;

namespace KeyLoad.ComparisonHost.Features.BenchmarkOrchestration;

/// <summary>Retains a bounded prefix while joining the complete original redirected stream.</summary>
internal static class ComparisonHostOutputCapture
{
    private const int MaximumCapturedCharacters = 32_768;
    private const int CaptureChunkCharacters = 4_096;
    private const int NoCharacters = 0;

    internal static async Task<string> CaptureAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var text = new StringBuilder();
        var buffer = new char[CaptureChunkCharacters];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) > NoCharacters)
        {
            var remaining = MaximumCapturedCharacters - text.Length;
            if (remaining > NoCharacters)
            {
                text.Append(buffer, NoCharacters, Math.Min(count, remaining));
            }
        }
        return text.ToString();
    }
}
