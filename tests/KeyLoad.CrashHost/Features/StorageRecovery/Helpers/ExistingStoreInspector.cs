namespace KeyLoad.CrashHost;

internal static class ExistingStoreInspector
{
    private const int InputChunkCharacters = 256;
    private const string OversizedRequest = "The original store request exceeds its bounded protocol.";
    internal static async Task<bool> TryRunAsync(string[] args)
    {
        if (args.Length != 1 || !string.Equals(args[0], ExistingStoreInspectorProtocol.Mode, StringComparison.Ordinal))
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
            GC.KeepAlive(failure.InnerExceptions[0]);
        }
        return true;
    }

    private static async Task InvokeModeAsync(TextWriter output, TextWriter error)
    {
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
            Environment.ExitCode = 0;
        }
        catch (Exception original)
        {
            throw new AggregateException(original);
        }
    }

    private static async Task<ExistingStoreInspectionRequest> ReadRequestAsync()
    {
        var retained = new char[ExistingStoreInspectorProtocol.MaximumRequestCharacters];
        var chunk = new char[InputChunkCharacters];
        var length = 0;
        var overflow = false;
        while (true)
        {
            var read = await Console.In.ReadAsync(chunk.AsMemory());
            if (read == 0)
            {
                if (overflow)
                {
                    throw new InvalidDataException(OversizedRequest);
                }
                return ExistingStoreInspectorProtocol.DeserializeRequest(new string(retained, 0, length));
            }
            var kept = Math.Min(read, retained.Length - length);
            chunk.AsSpan(0, kept).CopyTo(retained.AsSpan(length));
            length += kept;
            overflow |= kept != read;
        }
    }
}
