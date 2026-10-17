namespace KeyLoad.CrashHost;

internal static class ExistingStoreInspector
{
    private const string OversizedRequest = "The original store request exceeds its bounded protocol.";
    internal static async Task<bool> TryRunAsync(string[] args)
    {
        const int EmptyArgsLength = 1;
        const int ArgsFirstIndex = 0;
        const int IndexEmptyCount = 0;

        if (args.Length != EmptyArgsLength || !string.Equals(args[ArgsFirstIndex], ExistingStoreInspectorProtocol.Mode, StringComparison.Ordinal))
        {
            return false;
        }
        var output = Console.Out;
        var error = Console.Error;
        try
        {
            await InvokeModeAsync(output, error);
        }
        catch (AggregateException failure)
        {
            Environment.ExitCode = ExistingStoreInspectorProtocol.InvalidProtocolExitCode;
            GC.KeepAlive(failure.InnerExceptions[IndexEmptyCount]);
        }
        return true;
    }

    private static async Task InvokeModeAsync(TextWriter output, TextWriter error)
    {
        const int ExitCodeEmptyCount = 0;

        try
        {
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);
            var request = await ReadRequestAsync();
            if (request.Variant == ExistingStoreInspectionVariant.WaitBeforeOpen)
            {
                await error.WriteLineAsync(ExistingStoreInspectorProtocol.ReadyMarker);
                await error.FlushAsync();
                await Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System);
                return;
            }
            var receipt = ExistingStoreInspectorOperation.Run(request);
            var json = ExistingStoreInspectorProtocol.SerializeReceipt(receipt);
            await output.WriteLineAsync(json);
            await output.FlushAsync();
            Environment.ExitCode = ExitCodeEmptyCount;
        }
        catch (Exception original)
        {
            throw new AggregateException(original);
        }
    }

    private static async Task<ExistingStoreInspectionRequest> ReadRequestAsync()
    {
        const int LengthInitialValue = 0;
        const int EmptyRead = 0;
        const int StartIndexEmptyCount = 0;
        const int StartEmptyCount = 0;

        var retained = new char[ExistingStoreInspectorProtocol.MaximumRequestCharacters];
        var chunk = new char[CrashExecutionOptions.Child().Value.ProfileReadChunkCharacters];
        var length = LengthInitialValue;
        var overflow = false;
        while (true)
        {
            var read = await Console.In.ReadAsync(chunk.AsMemory());
            if (read == EmptyRead)
            {
                if (overflow)
                {
                    throw new InvalidDataException(OversizedRequest);
                }
                return ExistingStoreInspectorProtocol.DeserializeRequest(new string(retained, StartIndexEmptyCount, length));
            }
            var kept = Math.Min(read, retained.Length - length);
            chunk.AsSpan(StartEmptyCount, kept).CopyTo(retained.AsSpan(length));
            length += kept;
            overflow |= kept != read;
        }
    }
}
