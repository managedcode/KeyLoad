using System.Text;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class OwnerCapacityNativeFile
{
    private const int MaximumDescriptorBytes = 4096;
    private const int BoundaryLookahead = 1;
    private const int EmptyRead = 0;

    internal static async Task<string?> ReadAsync(string path, IOptions<TenantPlacementExecutionOptions> options, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            options.Value.DescriptorBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var bytes = new byte[MaximumDescriptorBytes + BoundaryLookahead];
        var count = EmptyRead;
        while (count < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(count), token).ConfigureAwait(false);
            if (read == EmptyRead)
            {
                return Encoding.UTF8.GetString(bytes.AsSpan(EmptyRead, count));
            }
            count = checked(count + read);
        }
        return null;
    }
}
