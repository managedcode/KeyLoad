using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferStartupArtifacts
{
    private const string DirectoryName = "kl094-original-startup";
    private const string IdentityFormat = "N";
    private const string InitiatingDirectory = "initiating";

    internal static void Save(Guid session, RemoteTransferStartupWindow window, string boundary,
        string primary, ErrorCode? code, bool cancelled, Exception? originalFailure = null)
    {
        var root = Path.Combine(ClusterFixtureDiagnostics.FindRepositoryRoot().FullName,
            ClusterFixtureProtocol.ArtifactDirectory, ClusterFixtureProtocol.QualificationDirectory,
            DirectoryName, session.ToString(IdentityFormat), Guid.NewGuid().ToString(IdentityFormat));
        if (originalFailure is not null)
        {
            var initiating = Path.Combine(root, InitiatingDirectory);
            Directory.CreateDirectory(initiating);
            _ = ClusterFailureReceipts.SaveImmutable(initiating,
                BoundedDiagnosticLog.Bound(RemoteTransferStartupFailureLines.Read(originalFailure)));
        }
        foreach (var item in window.Buffers)
        {
            var path = Path.Combine(root, item.Key);
            Directory.CreateDirectory(path);
            _ = ClusterFailureReceipts.SaveImmutable(path, item.Value.Snapshot(boundary, primary, code, cancelled));
        }
    }
}
