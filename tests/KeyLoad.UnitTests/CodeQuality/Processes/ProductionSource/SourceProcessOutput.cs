using System.Text;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal static class SourceProcessOutput
{
    private const string OutputFailure = "The execution source process exceeded its captured output bound.";
    private const int MinimumReadLength = 1;
    private const int InitialOutputCapacity = 512;

    internal static async Task<string> ReadBoundedAsync(StreamReader reader, int maximumCharacters)
    {
        var builder = new StringBuilder(Math.Min(maximumCharacters, InitialOutputCapacity));
        var buffer = new char[Math.Max(MinimumReadLength, maximumCharacters)];
        var exceeded = false;
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
            if (count == 0)
            {
                break;
            }
            if (builder.Length + count > maximumCharacters)
            {
                exceeded = true;
                builder.Append(buffer, 0, Math.Max(0, maximumCharacters - builder.Length));
                continue;
            }
            builder.Append(buffer, 0, count);
        }
        return exceeded ? throw new InvalidDataException(OutputFailure) : builder.ToString();
    }
}
