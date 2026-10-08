using KeyLoad.Server;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal static class C1OutcomeInspection
{

    internal static async Task<bool> TryRunAsync(string[] args)
    {
        const int EmptyArgsLength = 0;
        const int ArgsFirstIndex = 0;
        const int TryRunAsyncEmptyArgsLength = 1;
        const int ExitCodeEmptyCount = 0;

        if (args.Length == EmptyArgsLength || !string.Equals(args[ArgsFirstIndex], C1OutcomeInspectionProtocol.Mode, StringComparison.Ordinal))
        {
            return false;
        }
        var evidence = new C1OutcomeInspectionFailureEvidence();
        Console.SetOut(TextWriter.Null);
        Console.SetError(TextWriter.Null);
        try
        {
            evidence.SetPhase(C1OutcomeInspectionFailurePhase.ValidateRequest);
            if (args.Length != TryRunAsyncEmptyArgsLength)
            { throw new InvalidDataException(C1OutcomeInspectionProtocol.InvalidRequest); }
            evidence.SetPhase(C1OutcomeInspectionFailurePhase.ReadInput);
            var requestBytes = await ReadRequestBytesAsync().ConfigureAwait(false);
            evidence.SetPhase(C1OutcomeInspectionFailurePhase.ValidateRequest);
            var request = C1OutcomeInspectionJson.ReadRequest(requestBytes);
            var receipt = C1OutcomeInspectionOperation.Run(request, evidence);
            evidence.SetPhase(C1OutcomeInspectionFailurePhase.WriteReceipt);
            var receiptBytes = C1OutcomeInspectionJson.SerializeReceipt(receipt);
            await WriteBytesAsync(receiptBytes, standardError: false).ConfigureAwait(false);
            Environment.ExitCode = ExitCodeEmptyCount;
        }
        catch (Exception failure) when (!C1OutcomeInspectionFailures.ContainsFatal(failure))
        {
            evidence.Capture(failure);
            var failures = new List<Exception> { failure };
            await ServerFailureObserver.ObserveAsync(() => WriteBytesAsync(evidence.Bytes(), standardError: true),
                failures).ConfigureAwait(false);
            if (failures.Any(C1OutcomeInspectionFailures.ContainsFatal))
            { ServerFailureObserver.ThrowIfAny(failures); }
            Environment.ExitCode = C1OutcomeInspectionProtocol.FailureExitCode;
            GC.KeepAlive(failures);
        }
        return true;
    }

    private static async Task<byte[]> ReadRequestBytesAsync()
    {
        var failures = new List<Exception>();
        Stream? input = null;
        byte[]? requestBytes = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            input = Console.OpenStandardInput();
            requestBytes = await ReadInputAsync(input).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (input is not null)
        {
            var ownedInput = input;
            await ServerFailureObserver.ObserveAsync(() => ownedInput.DisposeAsync().AsTask(), failures)
                .ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return requestBytes ?? throw new InvalidDataException(C1OutcomeInspectionProtocol.InvalidRequest);
    }

    private static async Task<byte[]> ReadInputAsync(Stream input)
    {
        const int MaximumRequestBytesStep = 1;
        const int LengthInitialValue = 0;
        const int EmptyCount = 0;
        const int StartEmptyCount = 0;

        var retained = new byte[C1OutcomeInspectionProtocol.MaximumRequestBytes + MaximumRequestBytesStep];
        var chunk = new byte[CrashExecutionOptions.Child().Value.InspectionInputChunkBytes];
        var length = LengthInitialValue;
        while (true)
        {
            var count = await input.ReadAsync(chunk.AsMemory()).ConfigureAwait(false);
            if (count == EmptyCount)
            {
                break;
            }
            var kept = Math.Min(count, retained.Length - length);
            chunk.AsSpan(StartEmptyCount, kept).CopyTo(retained.AsSpan(length));
            length += kept;
        }
        if (length > C1OutcomeInspectionProtocol.MaximumRequestBytes)
        { throw new InvalidDataException(C1OutcomeInspectionProtocol.InvalidRequest); }
        return retained.AsSpan(StartEmptyCount, length).ToArray();
    }

    private static async Task WriteBytesAsync(byte[] receiptBytes, bool standardError)
    {
        var failures = new List<Exception>();
        Stream? output = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            output = standardError ? Console.OpenStandardError() : Console.OpenStandardOutput();
            await output.WriteAsync(receiptBytes.AsMemory()).ConfigureAwait(false);
            await output.FlushAsync().ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (output is not null)
        {
            var ownedOutput = output;
            await ServerFailureObserver.ObserveAsync(() => ownedOutput.DisposeAsync().AsTask(), failures)
                .ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
