namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Bounds child output without logging payloads and reports the exact private process marker.</summary>
internal sealed class ControlledPartitionMovementTerminalProcessPipe(string? expected, NativeMovementProcessOptions options)
{
    private readonly TaskCompletionSource signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task Signal => signal.Task;

    internal async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[options.ReadCharacters];
        var line = new System.Text.StringBuilder();
        var retained = 0;
        var exceeded = false;
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory());
            if (read == 0)
            { break; }
            retained = checked(retained + read);
            if (retained > options.OutputCharacters)
            { exceeded = true; }
            if (exceeded)
            { continue; }
            foreach (var character in buffer.AsSpan(0, read))
            { ConsumeCharacter(character, line); }
        }
        if (exceeded)
        { throw new IOException("Original movement child output exceeded its bound."); }
    }

    private void ConsumeCharacter(char character, System.Text.StringBuilder line)
    {
        if (character != '\n')
        { line.Append(character); return; }
        if (line.ToString().TrimEnd('\r') == expected)
        { signal.TrySetResult(); }
        line.Clear();
    }
}
