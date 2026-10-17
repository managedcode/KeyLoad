using System.Text;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteProcessOutput
{
    internal static async Task<string> ReadAsync(StreamReader reader, string exceededMessage,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var buffer = new char[SiteTokens.ProcessOutputBufferCharacters];
        var exceeded = false;
        int count;
        while ((count = await reader.ReadAsync(buffer, cancellationToken)) > SiteTokens.Zero)
        {
            if (!exceeded && output.Length + count <= SiteTokens.MaximumNodeOutputCharacters)
            {
                output.Append(buffer, SiteTokens.Zero, count);
            }
            else
            {
                exceeded = true;
            }
        }

        if (exceeded)
        {
            throw new InvalidOperationException(exceededMessage);
        }

        return output.ToString();
    }
}
