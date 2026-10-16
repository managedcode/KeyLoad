using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using static KeyLoad.Server.Features.ClusterRouting.RequestCqrsControlledLifetimeProtocol;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsControlledLifetimeParts
{
    internal static int Count(int bytes, int maximum) => bytes == EmptyBytes ? EmptyBytes
        : checked((bytes - IndexStep) / maximum + IndexStep);
    internal static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    internal static IEnumerable<(string Name, byte[] Bytes)> Split(Guid arm, Guid request, int step,
        string role, byte[] bytes, int maximum)
    {
        for (var index = FirstIndex; index < Count(bytes.Length, maximum); index += IndexStep)
        {
            var offset = checked(index * maximum);
            yield return (RequestCqrsControlledLifetimeNames.PartName(arm, request, step, role, index),
                bytes.AsSpan(offset, Math.Min(maximum, bytes.Length - offset)).ToArray());
        }
    }

    internal static byte[] Join(IReadOnlyDictionary<string, byte[]> parts, Guid arm, Guid request,
        int step, string role, int length, int count, string digest, IOptions<RequestProbeExecutionOptions> options)
    {
        if (length <= EmptyBytes || length > options.Value.MaximumAggregateBytes
            || count != Count(length, options.Value.MaximumRecordBytes) || count >= options.Value.MaximumFiles)
        {
            throw InvalidProof();
        }
        var result = new byte[length];
        for (var index = FirstIndex; index < count; index += IndexStep)
        {
            var name = RequestCqrsControlledLifetimeNames.PartName(arm, request, step, role, index);
            var offset = checked(index * options.Value.MaximumRecordBytes);
            if (!parts.TryGetValue(name, out var part)
                || part.Length != Math.Min(options.Value.MaximumRecordBytes, length - offset))
            {
                throw InvalidProof();
            }
            part.CopyTo(result, offset);
        }
        if (Digest(result) != digest)
        {
            throw InvalidProof();
        }
        return result;
    }
    private static InvalidOperationException InvalidProof() => new(Invalid);
}
