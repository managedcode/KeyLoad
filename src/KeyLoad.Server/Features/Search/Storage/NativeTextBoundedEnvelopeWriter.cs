using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextBoundedEnvelopeWriter
{
    private const long AbsentFileLength = 0;
    internal static void Write(string path, byte[] bytes, NativeTextResourceOwnership resources,
        IOptions<NativeTextExecutionOptions> executionOptions)
        => resources.MutatePhysical(() => WriteOwned(path, bytes, resources, executionOptions));

    private static void WriteOwned(string path, byte[] bytes, NativeTextResourceOwnership resources,
        IOptions<NativeTextExecutionOptions> executionOptions)
    {
        var reservation = resources.ReserveFile(path, bytes.LongLength, () => ObserveLength(path));
        FileStream? output = null;
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() =>
        {
            output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                executionOptions.Value.FileBufferBytes, FileOptions.WriteThrough);
            output.Write(bytes);
            output.Flush(true);
            NativeTextFileIO.SetPrivateFileMode(path);
        }, failures);
        if (output is not null)
        {
            var joined = false;
            ServerFailureObserver.Observe(() => { output.Dispose(); joined = true; }, failures);
            if (joined)
            { ServerFailureObserver.Observe(reservation.CompleteAfterJoinedWrite, failures); }
        }
        // Failed construction/close leaves uncertain ownership reserved; the actual owner must settle it.
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static long ObserveLength(string path)
    {
        if (!File.Exists(path))
        { return AbsentFileLength; }
        NativeTextFileIO.VerifyRegularFile(path);
        return new FileInfo(path).Length;
    }
}
