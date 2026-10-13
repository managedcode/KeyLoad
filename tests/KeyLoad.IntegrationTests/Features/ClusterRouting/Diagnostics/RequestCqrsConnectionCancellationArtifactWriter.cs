using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsConnectionCancellationArtifactWriter
{
    private const int ArtifactVersion = 1;
    private const int MaximumKilobytes = 16;
    private const int BytesPerKilobyte = 1_024;
    private const int MaximumArtifactBytes = MaximumKilobytes * BytesPerKilobyte;
    private const int EndOfStream = -1;
    private const string RootDirectory = "artifacts";
    private const string DirectoryName = "qualification";
    private const string Prefix = "request-cqrs-native-connection-cancellations-";
    private const string Suffix = ".json";
    private const string WaveFormat = "N";
    private const string InvalidArtifact = "The bounded original connection cancellation artifact is invalid or changed.";

    internal static string Write(Guid waveId, RequestCqrsConnectionCancellationNodeSnapshot[] nodes)
    {
        var original = new RequestCqrsConnectionCancellationArtifact(ArtifactVersion, waveId, nodes);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(original);
        if (bytes.Length > MaximumArtifactBytes)
        { throw new InvalidOperationException(InvalidArtifact); }
        var directory = Path.Combine(ClusterFixtureDiagnostics.FindRepositoryRoot().FullName,
            RootDirectory, DirectoryName);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Prefix + waveId.ToString(WaveFormat) + Suffix);
        WriteAndVerify(path, bytes, original);
        return path;
    }

    private static void WriteAndVerify(string path, byte[] originalBytes,
        RequestCqrsConnectionCancellationArtifact original)
    {
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(originalBytes);
            stream.Flush(flushToDisk: true);
        }
        var actualBytes = new byte[originalBytes.Length];
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            stream.ReadExactly(actualBytes);
            if (stream.ReadByte() != EndOfStream)
            { throw new InvalidOperationException(InvalidArtifact); }
        }
        if (!actualBytes.AsSpan().SequenceEqual(originalBytes))
        { throw new InvalidOperationException(InvalidArtifact); }
        var actual = JsonSerializer.Deserialize<RequestCqrsConnectionCancellationArtifact>(actualBytes)
            ?? throw new InvalidOperationException(InvalidArtifact);
        if (actual.Version != original.Version || actual.WaveId != original.WaveId
            || actual.Nodes is null || actual.Nodes.Length != original.Nodes.Length
            || !actual.Nodes.Zip(original.Nodes).All(pair => Same(pair.First, pair.Second)))
        { throw new InvalidOperationException(InvalidArtifact); }
    }
    private static bool Same(RequestCqrsConnectionCancellationNodeSnapshot actual,
        RequestCqrsConnectionCancellationNodeSnapshot original)
        => actual.Node == original.Node && actual.Overflowed == original.Overflowed
            && actual.Records is not null && actual.Records.SequenceEqual(original.Records);
}
