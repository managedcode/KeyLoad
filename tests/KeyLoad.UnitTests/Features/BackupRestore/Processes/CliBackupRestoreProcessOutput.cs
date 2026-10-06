using System.Text;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class CliBackupRestoreProcessOutput
{
    private const string OutputLimitMessage = "The Release KeyLoad CLI output exceeded the validated retention limit.";
    private readonly int maximumCharacters;
    private readonly StringBuilder retained;
    private bool exceeded;

    internal string Text => retained.ToString();

    internal CliBackupRestoreProcessOutput(int maximumCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCharacters);
        this.maximumCharacters = maximumCharacters;
        retained = new(maximumCharacters);
    }

    internal async Task DrainAsync(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var buffer = new char[maximumCharacters];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
            if (count == 0)
            {
                break;
            }
            Retain(buffer, count);
        }
        if (exceeded)
        {
            throw new InvalidDataException(OutputLimitMessage);
        }
    }

    private void Retain(char[] buffer, int count)
    {
        var remaining = maximumCharacters - retained.Length;
        if (count > remaining)
        {
            exceeded = true;
        }
        if (remaining > 0)
        {
            retained.Append(buffer, 0, Math.Min(count, remaining));
        }
    }
}
