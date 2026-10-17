using KeyLoad.AppHost.Features.CodeQuality;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal static class NativeCoverageRf3BoundedFileReader
{
    internal static async Task<byte[]> ReadAsync(string path, long maximumBytes,
        IOptions<NativeCoverageExecutionOptions> options, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(options);
        var execution = options.Value;
        if (!execution.IsValid())
        {
            throw Invalid();
        }
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw Invalid();
        }

        var length = info.Length;
        if (length <= 0 || length > maximumBytes || length > int.MaxValue
            || (info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw Invalid();
        }

        var bytes = new byte[checked((int)length)];
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            execution.ReadBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(offset), token).ConfigureAwait(false);
            if (read == 0)
            {
                throw Invalid();
            }

            offset += read;
        }

        var extra = new byte[1];
        if (await stream.ReadAsync(extra, token).ConfigureAwait(false) != 0
            || stream.Length != length || File.GetLastWriteTimeUtc(path) != info.LastWriteTimeUtc)
        {
            throw Invalid();
        }
        return bytes;
    }

    private static InvalidOperationException Invalid() =>
        new(NativeCoverageRf3FixtureProtocol.InvalidContext);
}
