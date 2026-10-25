using System.Text;

namespace KeyLoad.ProcessRecoveryTests.DocumentStorage;

internal sealed class CommandIdempotencyProcessOutputReader
{
    private const int BufferCharacters = 512;
    private const string AcknowledgementFailure = "The original command process did not finish its pre-restart retries.";
    private const string ChildSignalFailure = "The original document recovery child did not emit its required boundary signal.";
    private const string OutputLimitFailure = "The document command RecoveryProcess output exceeded its fixed bound.";
    private int witnessReported;
    private const string RetainedErrorMarker = "KEYLOAD_ORIGINAL_CHILD_STDERR";
    private readonly bool retainOutput;
    private StringBuilder? retained;
    private int retainedReported;
    private readonly string? expectedSignal;
    private readonly TaskCompletionSource<Exception> pipeFailure;
    private readonly TaskCompletionSource<bool> acknowledgement = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal CommandIdempotencyProcessOutputReader(int maximumCharacters, string? expectedSignal,
        TaskCompletionSource<Exception> pipeFailure, bool retainOutput = false)
    {
        WitnessLimit = maximumCharacters;
        this.expectedSignal = expectedSignal;
        this.pipeFailure = pipeFailure;
        this.retainOutput = retainOutput;
    }

    internal Task<bool> Acknowledgement => acknowledgement.Task;
    internal int WitnessLimit
    {
        get;
    }
    internal int WitnessTotal
    {
        get; private set;
    }
    internal int WitnessFirstCrossing
    {
        get; private set;
    }
    internal Exception? WitnessEarlyFailure
    {
        get; private set;
    }
    internal Exception? WitnessTerminalFailure
    {
        get; private set;
    }
    internal bool TryMarkWitnessReported() => Interlocked.Exchange(ref witnessReported, 1) == 0;

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

    internal void ReportRetainedOutput()
    {
        if (retained is null || Interlocked.Exchange(ref retainedReported, 1) != 0)
        {
            return;
        }
        Console.Error.WriteLine(FormattableString.Invariant(
            $"{RetainedErrorMarker} observedCharacters={WitnessTotal} retainedCharacters={retained.Length} truncated={WitnessTotal > retained.Length}"));
        Console.Error.Write(retained.ToString());
    }

    internal void FailAcknowledgement(Exception failure)
    {
        if (expectedSignal is not null)
        {
            acknowledgement.TrySetException(failure);
        }
    }

    private async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[BufferCharacters];
        var line = expectedSignal is not null ? new StringBuilder(expectedSignal.Length) : null;
        var characters = 0;
        var acknowledgementSeen = false;
        var invalidAcknowledgement = false;
        var exceeded = false;
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), CancellationToken.None)) != 0)
        {
            characters = checked(characters + read);
            WitnessTotal = characters;
            if (retainOutput && (retained?.Length ?? 0) < WitnessLimit)
            {
                retained ??= new StringBuilder();
                retained.Append(buffer, 0, Math.Min(read, WitnessLimit - retained.Length));
            }
            if (!exceeded)
            {
                exceeded = RecordOutputLimit(characters);
            }
            if (expectedSignal is not null && !acknowledgementSeen)
            {
                ObserveAcknowledgement(buffer, read, line!, ref acknowledgementSeen, ref invalidAcknowledgement);
            }
        }
        if (expectedSignal is not null && !acknowledgementSeen)
        {
            RecordAcknowledgementFailure();
            invalidAcknowledgement = true;
        }
        if (exceeded)
        {
            var failure = new InvalidOperationException(OutputLimitFailure);
            WitnessTerminalFailure = failure;
            throw failure;
        }
        if (invalidAcknowledgement)
        {
            throw await pipeFailure.Task;
        }
    }

    private bool RecordOutputLimit(int characters)
    {
        if (characters <= WitnessLimit)
        {
            return false;
        }
        var failure = new InvalidOperationException(OutputLimitFailure);
        WitnessEarlyFailure = failure;
        WitnessFirstCrossing = characters;
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
            else if (line.Length <= expectedSignal!.Length)
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
        if (string.Equals(line.ToString(), expectedSignal, StringComparison.Ordinal))
        {
            acknowledgement.TrySetResult(true);
            return;
        }
        invalidAcknowledgement = true;
        RecordAcknowledgementFailure();
    }

    private void RecordAcknowledgementFailure()
    {
        var message = expectedSignal == CrashFixtureValues.Acknowledgement
            ? AcknowledgementFailure
            : ChildSignalFailure;
        var failure = new InvalidOperationException(message);
        pipeFailure.TrySetResult(failure);
        FailAcknowledgement(failure);
    }
}
