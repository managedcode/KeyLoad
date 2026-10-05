using KeyLoad.Server;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal static class C1OutcomeInspection
{
    private const int InputChunkBytes = 1_024;

    internal static async Task<bool> TryRunAsync(string[] args)
    {
        if (args.Length == 0 || !string.Equals(args[0], C1OutcomeInspectionProtocol.Mode, StringComparison.Ordinal))
        {
            return false;
        }
        Console.SetOut(TextWriter.Null);
        Console.SetError(TextWriter.Null);
        try
        {
            if (args.Length != 1)
            { throw new InvalidDataException(C1OutcomeInspectionProtocol.InvalidRequest); }
            var requestBytes = await ReadRequestBytesAsync().ConfigureAwait(false);
            var request = C1OutcomeInspectionJson.ReadRequest(requestBytes);
            var receipt = C1OutcomeInspectionOperation.Run(request);
            var receiptBytes = C1OutcomeInspectionJson.SerializeReceipt(receipt);
            await WriteReceiptBytesAsync(receiptBytes).ConfigureAwait(false);
            Environment.ExitCode = 0;
        }
        catch (Exception failure) when (!C1OutcomeInspectionFailures.ContainsFatal(failure))
        {
            Environment.ExitCode = C1OutcomeInspectionProtocol.FailureExitCode;
            GC.KeepAlive(failure);
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
        var retained = new byte[C1OutcomeInspectionProtocol.MaximumRequestBytes + 1];
        var chunk = new byte[InputChunkBytes];
        var length = 0;
        while (true)
        {
            var count = await input.ReadAsync(chunk.AsMemory()).ConfigureAwait(false);
            if (count == 0)
            {
                break;
            }
            var kept = Math.Min(count, retained.Length - length);
            chunk.AsSpan(0, kept).CopyTo(retained.AsSpan(length));
            length += kept;
        }
        if (length > C1OutcomeInspectionProtocol.MaximumRequestBytes)
        { throw new InvalidDataException(C1OutcomeInspectionProtocol.InvalidRequest); }
        return retained.AsSpan(0, length).ToArray();
    }

    private static async Task WriteReceiptBytesAsync(byte[] receiptBytes)
    {
        var failures = new List<Exception>();
        Stream? output = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            output = Console.OpenStandardOutput();
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
