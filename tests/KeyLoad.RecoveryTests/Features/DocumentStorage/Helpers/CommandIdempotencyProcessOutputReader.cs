using System.Text;
using KeyLoad.CrashHost;

namespace KeyLoad.RecoveryTests.Features.DocumentStorage;

internal sealed class CommandIdempotencyProcessOutputReader
{
    private const int BufferCharacters = 512;
    private const string AcknowledgementFailure = "The original command process did not finish its pre-restart retries.";
    private const string OutputLimitFailure = "The document command CrashHost output exceeded its fixed bound.";
    private readonly int maximumCharacters;
    private readonly bool inspectAcknowledgement;
    private readonly TaskCompletionSource<Exception> pipeFailure;
    private readonly TaskCompletionSource<bool> acknowledgement = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal CommandIdempotencyProcessOutputReader(int maximumCharacters, bool inspectAcknowledgement,
        TaskCompletionSource<Exception> pipeFailure)
    {
        this.maximumCharacters = maximumCharacters;
        this.inspectAcknowledgement = inspectAcknowledgement;
        this.pipeFailure = pipeFailure;
    }

    internal Task<bool> Acknowledgement => acknowledgement.Task;

    internal async Task ReadAsync(StreamReader reader)
    {
        try
        {
            await DrainAsync(reader);
        }
        catch (Exception failure) when (CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
        {
            pipeFailure.TrySetResult(failure);
            FailAcknowledgement(failure);
            throw;
        }
        catch (Exception failure)
        {
            pipeFailure.TrySetResult(failure);
            FailAcknowledgement(failure);
            throw;
        }
    }

    internal void FailAcknowledgement(Exception failure)
    {
        if (inspectAcknowledgement)
        {
            acknowledgement.TrySetException(failure);
        }
    }

    private async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[BufferCharacters];
        var line = inspectAcknowledgement ? new StringBuilder(CrashFixtureValues.Acknowledgement.Length) : null;
        var characters = 0;
        var acknowledgementSeen = false;
        var invalidAcknowledgement = false;
        var exceeded = false;
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), CancellationToken.None)) != 0)
        {
            characters = checked(characters + read);
            if (!exceeded)
            {
                exceeded = RecordOutputLimit(characters);
            }
            if (inspectAcknowledgement && !acknowledgementSeen)
            {
                ObserveAcknowledgement(buffer, read, line!, ref acknowledgementSeen, ref invalidAcknowledgement);
            }
        }
        if (inspectAcknowledgement && !acknowledgementSeen)
        {
            RecordAcknowledgementFailure();
            invalidAcknowledgement = true;
        }
        if (exceeded)
        {
            throw new InvalidOperationException(OutputLimitFailure);
        }
        if (invalidAcknowledgement)
        {
            throw await pipeFailure.Task;
        }
    }

    private bool RecordOutputLimit(int characters)
    {
        if (characters <= maximumCharacters)
        {
            return false;
        }
        var failure = new InvalidOperationException(OutputLimitFailure);
        pipeFailure.TrySetResult(failure);
        FailAcknowledgement(failure);
        return true;
    }

    private void ObserveAcknowledgement(char[] buffer, int length, StringBuilder line,
        ref bool acknowledgementSeen, ref bool invalidAcknowledgement)
    {
        for (var index = 0; index < length && !acknowledgementSeen; index++)
        {
            var character = buffer[index];
            if (character == '\n')
            {
                ValidateAcknowledgement(line, ref acknowledgementSeen, ref invalidAcknowledgement);
            }
            else if (line.Length <= CrashFixtureValues.Acknowledgement.Length)
            {
                line.Append(character);
            }
            else
            {
                invalidAcknowledgement = true;
            }
        }
    }

    private void ValidateAcknowledgement(StringBuilder line, ref bool acknowledgementSeen,
        ref bool invalidAcknowledgement)
    {
        if (line.Length > 0 && line[^1] == '\r')
        {
            line.Length--;
        }
        acknowledgementSeen = true;
        if (string.Equals(line.ToString(), CrashFixtureValues.Acknowledgement, StringComparison.Ordinal))
        {
            acknowledgement.TrySetResult(true);
            return;
        }
        invalidAcknowledgement = true;
        RecordAcknowledgementFailure();
    }

    private void RecordAcknowledgementFailure()
    {
        var failure = new InvalidOperationException(AcknowledgementFailure);
        pipeFailure.TrySetResult(failure);
        FailAcknowledgement(failure);
    }
}
