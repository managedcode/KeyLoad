namespace KeyLoad.RecoveryTests.Features.DocumentStorage;

internal static class CommandIdempotencyProcessOutputWitness
{
    private const int HoldMode = 0;
    private const int ObserveMode = 1;
    private const int StandardOutput = 0;
    private const int StandardError = 1;
    private const int NoCrossing = 0;
    private const string Prefix = "KEYLOAD_NATIVE_CHILD_OUTPUT";
    private const string ModeField = " mode=";
    private const string PipeField = " pipe=";
    private const string LimitField = " limit=";
    private const string TotalField = " total=";
    private const string FirstCrossingField = " firstCrossing=";
    private const string TaskField = " task=";
    private const string StatusField = " status=";
    private const string EarlyField = " early=";
    private const string TerminalField = " terminal=";
    private const string SameField = " same=";

    internal static async Task ReportAsync(bool? hold,
        CommandIdempotencyProcessOutputReader? output, Task? originalOutput,
        CommandIdempotencyProcessOutputReader? error, Task? originalError, List<Exception> failures)
    {
        if (hold is not { } selected)
        {
            return;
        }
        var mode = selected ? HoldMode : ObserveMode;
        await PipeAsync(mode, StandardOutput, output, originalOutput, failures);
        await PipeAsync(mode, StandardError, error, originalError, failures);
    }

    private static async Task PipeAsync(int mode, int pipe,
        CommandIdempotencyProcessOutputReader? reader, Task? original, List<Exception> failures)
    {
        if (reader is null || original is not { IsCompleted: true }
            || original.IsCompletedSuccessfully || reader.WitnessFirstCrossing <= NoCrossing
            || !reader.TryMarkWitnessReported())
        {
            return;
        }
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(
            () => WriteAsync(mode, pipe, reader, original), failures);
    }

    private static Task WriteAsync(int mode, int pipe,
        CommandIdempotencyProcessOutputReader reader, Task original)
    {
        var early = reader.WitnessEarlyFailure;
        var terminal = reader.WitnessTerminalFailure;
        var same = early is not null && terminal is not null && ReferenceEquals(early, terminal);
        return Console.Error.WriteLineAsync(FormattableString.Invariant($"{Prefix}{ModeField}{mode}{PipeField}{pipe}{LimitField}{reader.WitnessLimit}{TotalField}{reader.WitnessTotal}{FirstCrossingField}{reader.WitnessFirstCrossing}{TaskField}{original.Id}{StatusField}{(int)original.Status}{EarlyField}{early is not null}{TerminalField}{terminal is not null}{SameField}{same}"));
    }
}
