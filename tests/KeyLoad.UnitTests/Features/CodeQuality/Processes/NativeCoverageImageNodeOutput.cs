using System.Text;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal static class NativeCoverageImageNodeOutput
{
    private const string OutputFailure = "The native coverage image materializer exceeded its captured output bound.";

    internal static async Task<string> ReadBoundedAsync(StreamReader reader, int maximumCharacters)
    {
        var builder = new StringBuilder(Math.Min(maximumCharacters, NativeCoverageImageConstants.InitialOutputCapacity));
        var buffer = new char[maximumCharacters];
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
