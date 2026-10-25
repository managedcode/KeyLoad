
namespace KeyLoad.ClusterTests.OrleansExecution.Helpers;

internal static class RequestCqrsClusterDiagnosticsSuccessEvidence
{
    internal static async Task AssertUnavailableBeforeJoinAsync(RequestCqrsClusterDiagnosticsTestScope scope)
    {
        var rejected = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => SaveEvidenceAsync(scope.Capture));
        if (rejected is null || File.Exists(scope.OwnedArtifactPath))
        {
            throw new InvalidOperationException("Unjoined diagnostics unexpectedly produced success evidence.");
        }
    }

    internal static async Task<(string Path, byte[] Bytes)> CompleteAndReadMemoizedAsync(
        RequestCqrsClusterDiagnosticsTestScope scope, CancellationToken cancellationToken)
    {
        await scope.JoinOriginalSubscriptionsAsync().ConfigureAwait(false);
        var capture = scope.Capture;
        var firstPath = capture.SaveEvidence();
        var firstBytes = await File.ReadAllBytesAsync(firstPath, cancellationToken).ConfigureAwait(false);
        var secondPath = capture.SaveEvidence();
        var secondBytes = await File.ReadAllBytesAsync(secondPath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(firstPath, scope.OwnedArtifactPath, StringComparison.Ordinal)
            || !string.Equals(secondPath, firstPath, StringComparison.Ordinal)
            || !firstBytes.AsSpan().SequenceEqual(secondBytes))
        {
            throw new InvalidOperationException("Repeated success evidence changed its path or bytes.");
        }
        return (firstPath, firstBytes);
    }

    private static async Task SaveEvidenceAsync(RequestCqrsClusterDiagnostics capture)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        _ = capture.SaveEvidence();
    }
}
