using System.Text;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteIsolatedGitHubProcessOutput
{
    public const int MaximumOutputCharacters = 4_195_328;
    public const int MaximumErrorCharacters = 262_144;

    public static async Task<string> ReadAsync(StreamReader reader, int maximum, CancellationToken token)
    {
        var result = new StringBuilder();
        var buffer = new char[SiteTokens.ProcessOutputBufferCharacters];
        var exceeded = false;
        int count;
        while ((count = await reader.ReadAsync(buffer, token)) > SiteIsolatedGitHubTokens.Zero)
        {
            if (!exceeded && result.Length + count <= maximum)
            {
                result.Append(buffer, SiteIsolatedGitHubTokens.Zero, count);
            }
            else
            {
                exceeded = true;
            }
        }

        if (exceeded)
        {
            throw new InvalidOperationException(SiteIsolatedGitHubTokens.NodeFailure);
        }

        return result.ToString();
    }
}
