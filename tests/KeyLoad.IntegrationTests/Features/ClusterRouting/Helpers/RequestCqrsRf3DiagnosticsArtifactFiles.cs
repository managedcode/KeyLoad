using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;

internal static class RequestCqrsRf3DiagnosticsArtifactFiles
{
    private const string DataRootArgument = "--KeyLoad:DataRoot=";
    private const string EphemeralArgument = "--KeyLoad:Ephemeral=true";
    private const string LoggerModelControlArgument = "--KeyLoadTests:LoggerModelControl=true";
    private const string DataRootPrefix = "keyload-c1-diagnostics-";
    private const string ExistingDataRootMessage = "The diagnostics data root already exists.";
    private const string ArtifactDirectory = "artifacts";
    private const string QualificationDirectory = "qualification";
    private const string ArtifactPrefix = "request-cqrs-rf3-mcp-rejections-";
    private const string ArtifactSuffix = ".json";
    private const string ArtifactDataKey = "KeyLoad.RequestCqrsRf3.McpRejectionArtifact";
    private const string FailureText = "The diagnostics test requests a bounded artifact.";
    internal const int MaximumArtifactBytes = 16 * 1_024;

    internal static string DataRootPath(Guid id)
        => Path.Combine(Path.GetTempPath(), DataRootPrefix + id.ToString("N"));

    internal static void CreateDataRootDirectory(string path)
    {
        if (Directory.Exists(path) || File.Exists(path))
        { throw new InvalidOperationException(ExistingDataRootMessage); }
        Directory.CreateDirectory(path);
    }

    internal static void RestrictDataRoot(string path)
    {
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
    }

    internal static string[] CreateAppHostArguments(string dataRoot)
        => [DataRootArgument + dataRoot, EphemeralArgument, LoggerModelControlArgument];

    internal static string ExpectedPath(Guid id)
        => Path.Combine(ClusterFixtureDiagnostics.FindRepositoryRoot().FullName, ArtifactDirectory,
            QualificationDirectory, ArtifactPrefix + id.ToString("N") + ArtifactSuffix);

    internal static async Task<byte[]> WriteAndReadAsync(RequestCqrsRf3Diagnostics diagnostics,
        string artifactPath, CancellationToken cancellationToken)
    {
        var failure = new InvalidOperationException(FailureText);
        diagnostics.SaveFailureEvidence(failure);
        var written = failure.Data[ArtifactDataKey] as string;
        if (!string.Equals(written, artifactPath, StringComparison.Ordinal))
        { throw new InvalidOperationException("The diagnostics artifact path did not match the owned wave."); }
        var bytes = await File.ReadAllBytesAsync(artifactPath, cancellationToken).ConfigureAwait(false);
        if (bytes.Length > MaximumArtifactBytes)
        { throw new InvalidOperationException("The diagnostics artifact exceeded its frozen byte bound."); }
        return bytes;
    }

    internal static void DeleteOwned(string? artifactPath, List<Exception> failures)
    {
        if (artifactPath is not null && File.Exists(artifactPath))
        { ServerFailureObserver.Observe(() => File.Delete(artifactPath), failures); }
    }
}
