using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal static class NativeCoverageRf3NodeReceiptBuilder
{
    internal static async Task<NativeCoverageRf3NodeReceipt> ReadAsync(string node,
        ContainerRuntimeInspection inspection, NativeCoverageRf3FixtureContext context,
        IOptions<NativeCoverageExecutionOptions> options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var execution = options.Value;
        if (!execution.IsValid())
        {
            throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidContext);
        }
        var coverageDirectory = Path.Combine(context.FixtureRoot,
            NativeCoverageRf3FixtureProtocol.CoverageDirectoryName, node);
        var terminalPath = Path.Combine(coverageDirectory, NativeCoverageRf3FixtureProtocol.TerminalReceiptName);
        var coveragePath = Path.Combine(coverageDirectory, NativeCoverageRf3FixtureProtocol.CoverageReportName);
        var contextPath = Path.Combine(context.ContextDirectory, NativeCoverageRf3FixtureProtocol.ContextManifestHashFilePath);
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
            throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidContext);
        }
        var process = ValidateTerminal(terminalBytes, node, inspection, context, coverageArtifact);
        return new(node, inspection.Id, inspection.ImageId, context.RunId, process.Pid, process.StartTicks,
            terminalArtifact, coverageArtifact, contextArtifact);
    }

    private static (long Pid, long StartTicks) ValidateTerminal(byte[] bytes, string node,
        ContainerRuntimeInspection inspection, NativeCoverageRf3FixtureContext context,
        NativeCoverageRf3ArtifactReference coverage)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        ValidateOriginalIdentity(root, node, inspection, context);
        ValidateCollectorSettlement(root, context.Bounds);
        var report = root.GetProperty(NativeCoverageRf3FixtureProtocol.ReportProperty);
        if (report.GetProperty(NativeCoverageRf3FixtureProtocol.FileNameProperty).GetString()
                != NativeCoverageRf3FixtureProtocol.CoverageReportName
            || report.GetProperty(NativeCoverageRf3FixtureProtocol.LengthProperty).GetInt64() != coverage.Length
            || report.GetProperty(NativeCoverageRf3FixtureProtocol.Sha256Property).GetString() != coverage.Sha256)
        {
            throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidContext);
        }
        var process = root.GetProperty(NativeCoverageRf3FixtureProtocol.ServerProcessProperty);
        if (process.GetProperty(NativeCoverageRf3FixtureProtocol.ExitStatusProperty).GetInt32() != 0)
        {
            throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidContext);
        }
        return (process.GetProperty(NativeCoverageRf3FixtureProtocol.PidProperty).GetInt64(),
            process.GetProperty(NativeCoverageRf3FixtureProtocol.StartTicksProperty).GetInt64());
    }

    private static void ValidateOriginalIdentity(JsonElement root, string node,
        ContainerRuntimeInspection inspection, NativeCoverageRf3FixtureContext context)
    {
        if (root.GetProperty(NativeCoverageRf3FixtureProtocol.SchemaVersionProperty).GetInt32()
                != NativeCoverageRf3FixtureProtocol.SchemaVersion
            || root.GetProperty(NativeCoverageRf3FixtureProtocol.NodeProperty).GetString() != node
            || root.GetProperty(NativeCoverageRf3FixtureProtocol.SessionProperty).GetString() != context.RunId
            || root.GetProperty(NativeCoverageRf3FixtureProtocol.ContextDigestProperty).GetString()
                != context.ContextManifestSha256
            || root.GetProperty(NativeCoverageRf3FixtureProtocol.ImageIdProperty).GetString() != inspection.ImageId
            || root.GetProperty(NativeCoverageRf3FixtureProtocol.ServerDllHashProperty).GetString()
                != context.Server.DllSha256
            || root.GetProperty(NativeCoverageRf3FixtureProtocol.ServerPdbHashProperty).GetString()
                != context.Server.PdbSha256
            || root.GetProperty(NativeCoverageRf3FixtureProtocol.MvidProperty).GetString() != context.Server.Mvid
            || root.GetProperty(NativeCoverageRf3FixtureProtocol.SourceReceiptSha256Property).GetString()
                != context.Server.SourceReceiptSha256
            || root.GetProperty(NativeCoverageRf3FixtureProtocol.ToolVersionProperty).GetString()
                != context.Collector.Version
            || root.GetProperty(NativeCoverageRf3FixtureProtocol.ToolClosureDigestProperty).GetString()
                != context.Collector.ClosureDigest
            || root.GetProperty(NativeCoverageRf3FixtureProtocol.SettingsSha256Property).GetString()
                != context.Collector.SettingsSha256)
        {
            throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidContext);
        }
    }

    private static void ValidateCollectorSettlement(JsonElement root, NativeCoverageRf3ExecutionBounds bounds)
    {
        if (root.GetProperty(NativeCoverageRf3FixtureProtocol.ShutdownSecondsProperty).GetInt64()
                != Seconds(bounds.ShutdownTimeout)
            || root.GetProperty(NativeCoverageRf3FixtureProtocol.SettlementSecondsProperty).GetInt64()
                != Seconds(bounds.SettlementTimeout)
            || root.GetProperty(NativeCoverageRf3FixtureProtocol.ShutdownCommandExitCodeProperty).GetInt32() != 0
            || root.GetProperty(NativeCoverageRf3FixtureProtocol.ConnectCommandExitCodeProperty).GetInt32() != 0
            || root.GetProperty(NativeCoverageRf3FixtureProtocol.CollectorCommandExitCodeProperty).GetInt32() != 0
            || root.GetProperty(NativeCoverageRf3FixtureProtocol.StopSignalProperty).GetString()
                != NativeCoverageRf3FixtureProtocol.StopTerm)
        {
            throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidContext);
        }
    }

    private static async Task<NativeCoverageRf3ArtifactReference> ReadArtifactAsync(string path,
        string evidenceRoot, long maximumBytes, int bufferBytes, CancellationToken token)
    {
        var fullPath = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(evidenceRoot, fullPath);
        if (Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidContext);
        }
        var before = new FileInfo(fullPath);
        if (!before.Exists || before.Length <= 0 || before.Length > maximumBytes)
        {
            throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidContext);
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
                throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidContext);
            }
            hash.AppendData(buffer, 0, read);
        }
        if (total != before.Length || stream.Length != before.Length)
        {
            throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidContext);
        }
        return new(relative.Replace(Path.DirectorySeparatorChar,
            NativeCoverageRf3FixtureProtocol.PathSeparator), total,
            Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    private static async Task<byte[]> ReadTerminalBytesAsync(string path, long maximumBytes, int bufferBytes,
        CancellationToken token)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is <= 0 || info.Length > maximumBytes || info.Length > int.MaxValue)
        {
            throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidContext);
        }
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferBytes,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var bytes = new byte[checked((int)info.Length)];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        if (stream.Length != bytes.LongLength)
        {
            throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidContext);
        }
        return bytes;
    }

    private static long Seconds(string value)
    {
        var duration = TimeSpan.ParseExact(value, "c", System.Globalization.CultureInfo.InvariantCulture);
        return checked((long)duration.TotalSeconds);
    }
}
