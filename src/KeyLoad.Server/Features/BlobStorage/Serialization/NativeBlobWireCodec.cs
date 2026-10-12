using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.BlobStorage;

internal sealed class NativeBlobWireCodec(IOptions<RequestProbeExecutionOptions> limits)
{
    private readonly NativeBlobWireJsonContext context = new(new JsonSerializerOptions
    {
        MaxDepth = limits.Value.MaximumJsonDepth,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    });

    internal byte[] Encode<T>(T value)
    {
        var info = Type<T>();
        using var output = new MemoryStream();
        using var bounded = new NativeBlobWireBoundedStream(output, limits.Value.MaximumRecordBytes);
        var failures = new List<Exception>();
        try
        {
            using var writer = new Utf8JsonWriter(bounded);
            ServerFailureObserver.Observe(() => { JsonSerializer.Serialize(writer, value, info); }, failures);
        }
        catch (Exception cleanup) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
        return output.ToArray();
    }

    internal T Read<T>(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty || bytes.Length > limits.Value.MaximumRecordBytes)
        { throw Invalid(); }
        var value = JsonSerializer.Deserialize(bytes, Type<T>()) ?? throw Invalid();
        if (!bytes.SequenceEqual(Encode(value)))
        { throw Invalid(); }
        return value;
    }

    internal static string Digest(ReadOnlySpan<byte> bytes)
        => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private JsonTypeInfo<T> Type<T>()
        => context.GetTypeInfo(typeof(T)) as JsonTypeInfo<T> ?? throw Invalid();

    private static InvalidOperationException Invalid() => new(NativeBlobWireProbeProtocol.InvalidEvidence);
}
