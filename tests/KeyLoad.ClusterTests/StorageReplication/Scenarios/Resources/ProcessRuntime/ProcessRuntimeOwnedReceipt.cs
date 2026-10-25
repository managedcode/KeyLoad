using System.Globalization;
using System.Text;
namespace KeyLoad.ClusterTests.StorageReplication;

internal static class ProcessRuntimeOwnedReceipt
{
    internal const string Prefix = "topic-sql-original-cold-owned-";
    private const string GuidFormat = "N";
    private const char Separator = '-';
    private const int GuidCharacters = 32;
    private const string Pending = ".pending";
    private const string Node1 = "node1";
    private const string Node2 = "node2";
    private const string Node3 = "node3";
    private const string Invalid = "The owned SQL cold receipt namespace is invalid.";

    internal static string Scenario(Guid incarnation, long ordinal)
    {
        if (incarnation == Guid.Empty || ordinal <= 0)
        {
            throw new InvalidOperationException(Invalid);
        }
        return Prefix + incarnation.ToString(GuidFormat) + Separator + ordinal.ToString(CultureInfo.InvariantCulture);
    }

    internal static string PathFor(string repository, string scenario, string resource)
    {
        if (!scenario.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(Invalid);
        }
        var suffix = scenario.AsSpan(Prefix.Length);
        if (suffix.Length <= GuidCharacters
            || suffix[GuidCharacters] != Separator
            || !Guid.TryParseExact(suffix[..GuidCharacters], GuidFormat, out var incarnation)
            || !long.TryParse(suffix[(GuidCharacters + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var ordinal)
            || !string.Equals(Scenario(incarnation, ordinal), scenario, StringComparison.Ordinal)
            || resource is not (Node1 or Node2 or Node3))
        {
            throw new InvalidOperationException(Invalid);
        }
        return Path.Combine(repository, ProcessRuntimeProtocol.ArtifactsDirectory,
            ProcessRuntimeProtocol.QualificationDirectory,
            string.Format(CultureInfo.InvariantCulture, ProcessRuntimeProtocol.ReceiptFileName, scenario, resource));
    }

    internal static async Task<bool> TryWriteAsync(ProcessRuntimeRestartReceipt receipt, string json, CancellationToken token)
    {
        if (!receipt.Scenario.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }
        var path = PathFor(receipt.RepositoryRoot, receipt.Scenario, receipt.ResourceName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var pending = path + Pending;
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var stream = new FileStream(pending, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous
            });
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes(json), token);
                await stream.FlushAsync(token);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        token.ThrowIfCancellationRequested();
        File.Move(pending, path, overwrite: false);
        return true;
    }
}
