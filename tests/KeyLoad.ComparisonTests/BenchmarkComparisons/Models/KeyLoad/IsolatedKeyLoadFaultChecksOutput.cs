using System.Text;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Bounded native readers keep draining without retaining oversized or secret-bearing process output.</summary>
internal sealed record IsolatedKeyLoadFaultChecksOutput(string Text, bool Oversized)
{
    internal static async Task<IsolatedKeyLoadFaultChecksOutput> ReadAsync(StreamReader reader, int limit)
    {
        var buffer = new char[1_024];
        var retained = new StringBuilder(Math.Min(limit, buffer.Length));
        var oversized = false;
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), CancellationToken.None);
            if (count == 0)
            {
                return new(retained.ToString(), oversized);
            }
            var available = Math.Max(0, limit - retained.Length);
            retained.Append(buffer, 0, Math.Min(available, count));
            oversized |= count > available;
        }
    }
}
