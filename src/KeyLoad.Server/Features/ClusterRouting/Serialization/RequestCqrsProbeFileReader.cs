using KeyLoad.Storage.IO;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Reads one private probe record within its centrally validated file budget.</summary>
internal static class RequestCqrsProbeFileReader
{
    internal static byte[] Read(string path, IOptions<RequestProbeExecutionOptions> executionOptions)
    {
        const int ReadInitialValue = 0;
        const int EmptyCount = 0;
        const int StartEmptyCount = 0;

        _ = OfflineRegularFile.Inspect(path);
        RequestCqrsProbePaths.RequirePrivateMode(path, RequestCqrsProbeProtocol.PrivateFileMode);
        using var stream = OfflineRegularFile.Open(path, FileAccess.Read, FileShare.Read, executionOptions.Value.ReadBufferBytes);
        var bytes = new byte[executionOptions.Value.ReadBufferBytes];
        var read = ReadInitialValue;
        while (read < bytes.Length)
        {
            var count = stream.Read(bytes, read, bytes.Length - read);
            if (count == EmptyCount)
            { break; }
            read += count;
        }
        if (read == bytes.Length)
        { throw Invalid(); }
        return bytes.AsSpan(StartEmptyCount, read).ToArray();
    }

    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidFiles);
}
