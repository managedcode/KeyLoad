using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

internal static class TextBoundedEnvelopeWriter
{
    private const long AbsentFileLength = 0;
    internal static void Write(string path, byte[] bytes, TextResourceOwnership resources,
        IOptions<TextExecutionOptions> executionOptions)
        => resources.MutatePhysical(() => WriteOwned(path, bytes, resources, executionOptions));

    private static void WriteOwned(string path, byte[] bytes, TextResourceOwnership resources,
        IOptions<TextExecutionOptions> executionOptions)
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
            TextFileIO.SetPrivateFileMode(path);
        }, failures);
        if (output is not null)
        {
            var joined = false;
            ServerFailureObserver.Observe(() => { output.Dispose(); joined = true; }, failures);
            if (joined)
            {
                ServerFailureObserver.Observe(reservation.CompleteAfterJoinedWrite, failures);
            }
        }
        // Failed construction/close leaves uncertain ownership reserved; the actual owner must settle it.
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static long ObserveLength(string path)
    {
        if (!File.Exists(path))
        {
            return AbsentFileLength;
        }
        TextFileIO.VerifyRegularFile(path);
        return new FileInfo(path).Length;
    }
}
