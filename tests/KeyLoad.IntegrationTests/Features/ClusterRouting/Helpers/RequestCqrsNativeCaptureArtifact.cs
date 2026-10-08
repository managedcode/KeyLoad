using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsNativeCaptureArtifact
{
    private const string Suffix = ".native-capture.json";
    private const int MaximumBytes = 16 * 1_024;
    private const int Version = 1;

    internal static string PathFor(string originalArtifact) => originalArtifact + Suffix;

    internal static void Write(string originalArtifact, Guid waveId, RequestCqrsNativeCaptureSnapshot[] nodes)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new RequestCqrsNativeCaptureArtifactRecord(Version, waveId, nodes));
        if (bytes.Length > MaximumBytes)
        { throw new InvalidDataException("Native C1 capture evidence exceeds its existing artifact budget."); }
        using var file = new FileStream(PathFor(originalArtifact), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(bytes);
        file.Flush(true);
    }
}

internal sealed record RequestCqrsNativeCaptureArtifactRecord(int Version, Guid WaveId, RequestCqrsNativeCaptureSnapshot[] Nodes);
