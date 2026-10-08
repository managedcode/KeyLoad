using KeyLoad.Server;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

/// <summary>Bounds exact original native process inputs/results without a public JSON or format fallback.</summary>
internal static class ControlledPartitionMovementProcessFiles
{
    private const long EmptyLength = 0;

    internal static async Task<T> ReadAsync<T>(string root, string name, CancellationToken cancellationToken)
        where T : class
    {
        T? result = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var input = new FileStream(Path.Combine(root, name), FileMode.Open, FileAccess.Read,
                FileShare.Read, CrashExecutionOptions.Child().Value.AuthorityReadBufferBytes, FileOptions.Asynchronous);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                if (input.Length is <= EmptyLength
                    || input.Length > CrashExecutionOptions.DatabaseLimits().Value.MaxBatchBytes)
                { throw new InvalidOperationException(ControlledPartitionMovementProcessProtocol.Invalid); }
                var bytes = new byte[checked((int)input.Length)];
                await input.ReadExactlyAsync(bytes, cancellationToken);
                result = NativeSerialization.Deserialize<T>(bytes);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException(ControlledPartitionMovementProcessProtocol.Invalid);
    }

    internal static async Task WriteAsync<T>(string root, string name, T value, CancellationToken cancellationToken)
        where T : class
    {
        var measured = NativeSerialization.Measure(value);
        if (measured is <= EmptyLength
            || measured > CrashExecutionOptions.DatabaseLimits().Value.MaxBatchBytes)
        { throw new InvalidOperationException(ControlledPartitionMovementProcessProtocol.Invalid); }
        var bytes = NativeSerialization.Serialize(value);
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = CrashExecutionOptions.Child().Value.AuthorityReadBufferBytes,
            Options = FileOptions.Asynchronous
        };
        if (!OperatingSystem.IsWindows())
        { options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite; }
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var output = new FileStream(Path.Combine(root, name), options);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await output.WriteAsync(bytes, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
