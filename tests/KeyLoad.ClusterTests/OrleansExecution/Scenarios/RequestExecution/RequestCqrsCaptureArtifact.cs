using System.Text.Json;

namespace KeyLoad.ClusterTests.OrleansExecution;

internal static class RequestCqrsCaptureArtifact
{
    private const string Suffix = ".execution-capture.json";
    private const int MaximumBytes = 16 * 1_024;
    private const int Version = 1;

    internal static string PathFor(string originalArtifact) => originalArtifact + Suffix;

    internal static void Write(string originalArtifact, Guid waveId, RequestCqrsCaptureSnapshot[] nodes)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new RequestCqrsCaptureArtifactRecord(Version, waveId, nodes));
        if (bytes.Length > MaximumBytes)
        {
            throw new InvalidDataException("Execution C1 capture evidence exceeds its existing artifact budget.");
        }
        using var file = new FileStream(PathFor(originalArtifact), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(bytes);
        file.Flush(true);
    }
}
