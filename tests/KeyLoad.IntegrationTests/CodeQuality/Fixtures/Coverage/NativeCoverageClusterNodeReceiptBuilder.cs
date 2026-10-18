using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal static class NativeCoverageClusterNodeReceiptBuilder
{
    internal static async Task<NativeCoverageClusterNodeReceipt> ReadAsync(string node,
        ContainerRuntimeInspection inspection, NativeCoverageClusterFixtureContext context,
        IOptions<NativeCoverageExecutionOptions> options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var execution = options.Value;
        if (!execution.IsValid())
        {
            throw new InvalidOperationException(NativeCoverageClusterFixtureProtocol.InvalidContext);
        }
        var coverageDirectory = Path.Combine(context.FixtureRoot,
            NativeCoverageClusterFixtureProtocol.CoverageDirectoryName, node);
        var terminalPath = Path.Combine(coverageDirectory, NativeCoverageClusterFixtureProtocol.TerminalReceiptName);
        var coveragePath = Path.Combine(coverageDirectory, NativeCoverageClusterFixtureProtocol.CoverageReportName);
        var contextPath = Path.Combine(context.ContextDirectory, NativeCoverageClusterFixtureProtocol.ContextManifestHashFilePath);
        var terminalArtifact = await ReadArtifactAsync(terminalPath, context.EvidenceRoot,
            context.Bounds.MaximumManifestBytes, execution.ReadBufferBytes, cancellationToken).ConfigureAwait(false);
        var coverageArtifact = await ReadArtifactAsync(coveragePath, context.EvidenceRoot,
            context.Bounds.MaximumReportBytes, execution.ReadBufferBytes, cancellationToken).ConfigureAwait(false);
        var contextArtifact = await ReadArtifactAsync(contextPath, context.EvidenceRoot,
            context.Bounds.MaximumManifestBytes, execution.ReadBufferBytes, cancellationToken).ConfigureAwait(false);
        var terminalBytes = await ReadTerminalBytesAsync(terminalPath,
            context.Bounds.MaximumManifestBytes, execution.ReadBufferBytes, cancellationToken).ConfigureAwait(false);
        if (terminalBytes.LongLength != terminalArtifact.Length
            || Convert.ToHexStringLower(SHA256.HashData(terminalBytes)) != terminalArtifact.Sha256)
        {
            throw new InvalidOperationException(NativeCoverageClusterFixtureProtocol.InvalidContext);
        }
        var process = ValidateTerminal(terminalBytes, node, inspection, context, coverageArtifact);
        return new(node, inspection.Id, inspection.ImageId, context.RunId, process.Pid, process.StartTicks,
            terminalArtifact, coverageArtifact, contextArtifact);
    }

    private static (long Pid, long StartTicks) ValidateTerminal(byte[] bytes, string node,
        ContainerRuntimeInspection inspection, NativeCoverageClusterFixtureContext context,
        NativeCoverageClusterArtifactReference coverage)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        ValidateOriginalIdentity(root, node, inspection, context);
        ValidateCollectorSettlement(root, context.Bounds);
        var report = root.GetProperty(NativeCoverageClusterFixtureProtocol.ReportProperty);
        if (report.GetProperty(NativeCoverageClusterFixtureProtocol.FileNameProperty).GetString()
                != NativeCoverageClusterFixtureProtocol.CoverageReportName
            || report.GetProperty(NativeCoverageClusterFixtureProtocol.LengthProperty).GetInt64() != coverage.Length
            || report.GetProperty(NativeCoverageClusterFixtureProtocol.Sha256Property).GetString() != coverage.Sha256)
        {
            throw new InvalidOperationException(NativeCoverageClusterFixtureProtocol.InvalidContext);
        }
        var process = root.GetProperty(NativeCoverageClusterFixtureProtocol.ServerProcessProperty);
        if (process.GetProperty(NativeCoverageClusterFixtureProtocol.ExitStatusProperty).GetInt32() != 0)
        {
            throw new InvalidOperationException(NativeCoverageClusterFixtureProtocol.InvalidContext);
        }
        return (process.GetProperty(NativeCoverageClusterFixtureProtocol.PidProperty).GetInt64(),
            process.GetProperty(NativeCoverageClusterFixtureProtocol.StartTicksProperty).GetInt64());
    }

    private static void ValidateOriginalIdentity(JsonElement root, string node,
        ContainerRuntimeInspection inspection, NativeCoverageClusterFixtureContext context)
    {
        if (root.GetProperty(NativeCoverageClusterFixtureProtocol.SchemaVersionProperty).GetInt32()
                != NativeCoverageClusterFixtureProtocol.SchemaVersion
            || root.GetProperty(NativeCoverageClusterFixtureProtocol.NodeProperty).GetString() != node
            || root.GetProperty(NativeCoverageClusterFixtureProtocol.SessionProperty).GetString() != context.RunId
            || root.GetProperty(NativeCoverageClusterFixtureProtocol.ContextDigestProperty).GetString()
                != context.ContextManifestSha256
            || root.GetProperty(NativeCoverageClusterFixtureProtocol.ImageIdProperty).GetString() != inspection.ImageId
            || root.GetProperty(NativeCoverageClusterFixtureProtocol.ServerDllHashProperty).GetString()
                != context.Server.DllSha256
            || root.GetProperty(NativeCoverageClusterFixtureProtocol.ServerPdbHashProperty).GetString()
                != context.Server.PdbSha256
            || root.GetProperty(NativeCoverageClusterFixtureProtocol.MvidProperty).GetString() != context.Server.Mvid
            || root.GetProperty(NativeCoverageClusterFixtureProtocol.SourceReceiptSha256Property).GetString()
                != context.Server.SourceReceiptSha256
            || root.GetProperty(NativeCoverageClusterFixtureProtocol.ToolVersionProperty).GetString()
                != context.Collector.Version
            || root.GetProperty(NativeCoverageClusterFixtureProtocol.ToolClosureDigestProperty).GetString()
                != context.Collector.ClosureDigest
            || root.GetProperty(NativeCoverageClusterFixtureProtocol.SettingsSha256Property).GetString()
                != context.Collector.SettingsSha256)
        {
            throw new InvalidOperationException(NativeCoverageClusterFixtureProtocol.InvalidContext);
        }
    }

    private static void ValidateCollectorSettlement(JsonElement root, NativeCoverageClusterExecutionBounds bounds)
    {
        if (root.GetProperty(NativeCoverageClusterFixtureProtocol.ShutdownSecondsProperty).GetInt64()
                != Seconds(bounds.ShutdownTimeout)
            || root.GetProperty(NativeCoverageClusterFixtureProtocol.SettlementSecondsProperty).GetInt64()
                != Seconds(bounds.SettlementTimeout)
            || root.GetProperty(NativeCoverageClusterFixtureProtocol.ShutdownCommandExitCodeProperty).GetInt32() != 0
            || root.GetProperty(NativeCoverageClusterFixtureProtocol.ConnectCommandExitCodeProperty).GetInt32() != 0
            || root.GetProperty(NativeCoverageClusterFixtureProtocol.CollectorCommandExitCodeProperty).GetInt32() != 0
            || root.GetProperty(NativeCoverageClusterFixtureProtocol.StopSignalProperty).GetString()
                != NativeCoverageClusterFixtureProtocol.StopTerm)
        {
            throw new InvalidOperationException(NativeCoverageClusterFixtureProtocol.InvalidContext);
        }
    }

    private static async Task<NativeCoverageClusterArtifactReference> ReadArtifactAsync(string path,
        string evidenceRoot, long maximumBytes, int bufferBytes, CancellationToken token)
    {
        var fullPath = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(evidenceRoot, fullPath);
        if (Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(NativeCoverageClusterFixtureProtocol.InvalidContext);
        }
        var before = new FileInfo(fullPath);
        if (!before.Exists || before.Length <= 0 || before.Length > maximumBytes)
        {
            throw new InvalidOperationException(NativeCoverageClusterFixtureProtocol.InvalidContext);
        }
        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[bufferBytes];
        long total = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            total = checked(total + read);
            if (total > maximumBytes)
            {
                throw new InvalidOperationException(NativeCoverageClusterFixtureProtocol.InvalidContext);
            }
            hash.AppendData(buffer, 0, read);
        }
        if (total != before.Length || stream.Length != before.Length)
        {
            throw new InvalidOperationException(NativeCoverageClusterFixtureProtocol.InvalidContext);
        }
        return new(relative.Replace(Path.DirectorySeparatorChar,
            NativeCoverageClusterFixtureProtocol.PathSeparator), total,
            Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    private static async Task<byte[]> ReadTerminalBytesAsync(string path, long maximumBytes, int bufferBytes,
        CancellationToken token)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is <= 0 || info.Length > maximumBytes || info.Length > int.MaxValue)
        {
            throw new InvalidOperationException(NativeCoverageClusterFixtureProtocol.InvalidContext);
        }
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferBytes,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var bytes = new byte[checked((int)info.Length)];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        if (stream.Length != bytes.LongLength)
        {
            throw new InvalidOperationException(NativeCoverageClusterFixtureProtocol.InvalidContext);
        }
        return bytes;
    }

    private static long Seconds(string value)
    {
        var duration = TimeSpan.ParseExact(value, "c", System.Globalization.CultureInfo.InvariantCulture);
        return checked((long)duration.TotalSeconds);
    }
}
