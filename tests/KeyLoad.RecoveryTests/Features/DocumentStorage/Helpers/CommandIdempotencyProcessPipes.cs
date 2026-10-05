using System.Diagnostics;

namespace KeyLoad.RecoveryTests.Features.DocumentStorage;

internal sealed class CommandIdempotencyProcessPipes
{
    private readonly StreamReader output;
    private readonly StreamReader error;
    private readonly int maximumCharacters;
    private readonly bool expectAcknowledgement;
    private readonly TaskCompletionSource<Exception> pipeFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CommandIdempotencyProcessOutputReader? outputReader;
    private Task? standardOutput;
    private Task? standardError;
    private Task? readersJoined;
    private Exception? outputStartupFailure;
    private Exception? errorStartupFailure;
    private Exception? joinStartupFailure;

    private CommandIdempotencyProcessPipes(StreamReader output, StreamReader error, int maximumCharacters,
        bool expectAcknowledgement)
    {
        this.output = output;
        this.error = error;
        this.maximumCharacters = maximumCharacters;
        this.expectAcknowledgement = expectAcknowledgement;
    }

    internal Task<Exception> FailureTask => pipeFailure.Task;
    internal bool ReadersSettled => (standardOutput is null || standardOutput.IsCompleted)
        && (standardError is null || standardError.IsCompleted);
    internal Exception? StartupFailure => PreserveFailures(outputStartupFailure, errorStartupFailure, joinStartupFailure);

    internal static CommandIdempotencyProcessPipes Create(Process process, int maximumCharacters,
        bool expectAcknowledgement)
        => new(process.StandardOutput, process.StandardError, maximumCharacters, expectAcknowledgement);

    internal void StartReaders()
    {
        StartOutputReader();
        StartErrorReader();
        try
        {
            if (standardOutput is not null && standardError is not null)
            {
                readersJoined = Task.WhenAll(standardOutput, standardError);
            }
        }
        catch (Exception failure) when (CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
        {
            joinStartupFailure = failure;
            SignalReaderStartFailure(failure, signalAcknowledgement: false);
        }
        catch (Exception failure) when (!CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
        {
            joinStartupFailure = failure;
            SignalReaderStartFailure(failure, signalAcknowledgement: false);
        }
    }

    internal async Task WaitForAcknowledgementAsync(CancellationToken cancellationToken)
    {
        var activeOutputReader = outputReader
            ?? throw new InvalidOperationException("The command output reader did not start.");
        var completed = await Task.WhenAny(activeOutputReader.Acknowledgement, pipeFailure.Task)
            .WaitAsync(cancellationToken);
        if (ReferenceEquals(completed, pipeFailure.Task))
        {
            throw await pipeFailure.Task;
        }
        await activeOutputReader.Acknowledgement;
    }

    internal async Task JoinAsync(CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        if (readersJoined is { } originalJoin)
        {
            await ObserveJoinAsync(originalJoin, failures, cancellationToken);
        }
        else
        {
            await ObserveJoinAsync(standardOutput, failures, cancellationToken);
            await ObserveJoinAsync(standardError, failures, cancellationToken);
        }
        AddReaderFailures(standardOutput, outputStartupFailure, failures);
        AddReaderFailures(standardError, errorStartupFailure, failures);
        if (joinStartupFailure is not null)
        {
            CommandIdempotencyProcessFailureHandling.AddDistinct(failures, joinStartupFailure);
        }
        CommandIdempotencyProcessFailureHandling.ThrowFailures(failures);
    }

    private void StartOutputReader()
    {
        try
        {
            outputReader = new CommandIdempotencyProcessOutputReader(maximumCharacters,
                expectAcknowledgement, pipeFailure);
            standardOutput = outputReader.ReadAsync(output);
        }
        catch (Exception failure) when (CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
        {
            outputStartupFailure = failure;
            SignalReaderStartFailure(failure, signalAcknowledgement: true);
        }
        catch (Exception failure) when (!CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
        {
            outputStartupFailure = failure;
            SignalReaderStartFailure(failure, signalAcknowledgement: true);
        }
    }

    private void StartErrorReader()
    {
        try
        {
            var errorReader = new CommandIdempotencyProcessOutputReader(maximumCharacters,
                inspectAcknowledgement: false, pipeFailure);
            standardError = errorReader.ReadAsync(error);
        }
        catch (Exception failure) when (CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
        {
            errorStartupFailure = failure;
            SignalReaderStartFailure(failure, signalAcknowledgement: false);
        }
        catch (Exception failure) when (!CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
        {
            errorStartupFailure = failure;
            SignalReaderStartFailure(failure, signalAcknowledgement: false);
        }
    }

    private void SignalReaderStartFailure(Exception failure, bool signalAcknowledgement)
    {
        pipeFailure.TrySetResult(failure);
        if (signalAcknowledgement)
        {
            outputReader?.FailAcknowledgement(failure);
        }
    }

    private static async Task ObserveJoinAsync(Task? original, List<Exception> failures,
        CancellationToken cancellationToken)
    {
        if (original is null)
        {
            return;
        }
        try
        {
            await original.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (!original.IsCompleted)
        {
            throw;
        }
        catch (Exception failure) when (CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
        {
            CommandIdempotencyProcessFailureHandling.AddDistinct(failures, failure);
        }
        catch (Exception failure) when (!CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
        {
            CommandIdempotencyProcessFailureHandling.AddDistinct(failures, failure);
        }
    }

    private static void AddReaderFailures(Task? reader, Exception? startupFailure, List<Exception> failures)
    {
        if (startupFailure is not null)
        {
            CommandIdempotencyProcessFailureHandling.AddDistinct(failures, startupFailure);
        }
        if (reader?.Exception is { } envelope)
        {
            foreach (var failure in envelope.InnerExceptions)
            {
                CommandIdempotencyProcessFailureHandling.AddDistinct(failures, failure);
            }
        }
        else if (reader?.IsCanceled == true)
        {
            failures.Add(new TaskCanceledException(reader));
        }
    }

    private static Exception? PreserveFailures(Exception? first, Exception? second, Exception? third)
    {
        var combined = CommandIdempotencyProcessFailureHandling.PreserveStartupFailure(first, second);
        return CommandIdempotencyProcessFailureHandling.PreserveStartupFailure(combined, third);
    }
}
