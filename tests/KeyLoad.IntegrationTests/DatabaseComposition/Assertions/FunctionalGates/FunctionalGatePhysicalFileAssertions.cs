using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.IO;
using static KeyLoad.IntegrationTests.Features.DatabaseComposition.FunctionalGatePhysicalFileProtocol;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

/// <summary>Observes actual files; it neither opens a live tree nor changes its ownership.</summary>
internal static class FunctionalGatePhysicalFileAssertions
{
    internal static async Task<FunctionalGatePhysicalFileEvidence> CaptureLiveAsync(FunctionalGateFixture fixture,
        CancellationToken token)
    {
        var owners = new List<FunctionalGatePhysicalFileOwner>();
        var nodeIds = new HashSet<Guid>();
        foreach (var name in fixture.NodeNames)
        {
            token.ThrowIfCancellationRequested();
            var runtime = await ClusterFixtureApplicationStartup.ReadAsync(fixture.App, name, token);
            var directory = FunctionalGateFileOwnershipAssertions.Directory(fixture, name);
            RequireDirectory(directory);
            var status = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, name, fixture.AdminKey,
                async caller =>
                {
                    var sdk = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
                    var mcp = (await McpCallerAssertions.SuccessAsync<NodeStatus>(await caller.Mcp.CallWithoutBodyAsync(
                        McpCallerTools.AdminStatus, token))).Value;
                    await Assert.That(mcp.NodeId).IsEqualTo(sdk.NodeId);
                    await Assert.That(mcp.Incarnation).IsEqualTo(sdk.Incarnation);
                    await Assert.That(sdk.ProcessId).IsEqualTo(runtime.ProcessId);
                    await Assert.That(mcp.ProcessId).IsEqualTo(runtime.ProcessId);
                    return sdk;
                }, token);
            if (!Guid.TryParse(status.NodeId, out var nodeId) || nodeId == Guid.Empty
                || status.Incarnation == Guid.Empty || !nodeIds.Add(nodeId))
            {
                throw new InvalidOperationException(Failure);
            }
            owners.Add(new(name, directory, nodeId, status.Incarnation, runtime));
        }
        return new([.. owners]);
    }

    internal static void RequireStopped(FunctionalGateFixture fixture, FunctionalGatePhysicalFileEvidence evidence)
    {
        if (evidence.Owners.Length != fixture.NodeNames.Length
            || !evidence.Owners.Select(item => item.Resource).Order(StringComparer.Ordinal)
                .SequenceEqual(fixture.NodeNames.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new InvalidOperationException(Failure);
        }
        var files = new HashSet<(ulong Device, ulong Inode)>();
        foreach (var owner in evidence.Owners)
        {
            RequireDirectory(owner.Directory);
            var canonical = Path.Combine(owner.Directory, Canonical);
            RequireIdentity(canonical, owner.NodeId, owner.Incarnation);
            RequireTree(canonical, files);
        }
    }

    private static void RequireIdentity(string directory, Guid? nodeId, Guid incarnation)
    {
        RequireDirectory(directory);
        var identity = ZoneTreeIdentityFile.Read(Path.Combine(directory, Identity), MaximumIdentityBytes, StreamBufferBytes);
        if (identity.Incarnation != incarnation || nodeId is { } expected && identity.NodeId != expected)
        {
            throw new InvalidOperationException(Failure);
        }
    }

    private static void RequireTree(string directory, HashSet<(ulong Device, ulong Inode)> files)
    {
        var pending = new Stack<string>();
        pending.Push(Path.Combine(directory, Tree));
        var entries = NoEntries;
        var fileCount = NoEntries;
        while (pending.TryPop(out var current))
        {
            RequireDirectory(current);
            foreach (var path in Directory.EnumerateFileSystemEntries(current))
            {
                if (++entries > MaximumEntries || path.Length > MaximumPathCharacters)
                {
                    throw new InvalidOperationException(Failure);
                }
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != NoEntries)
                {
                    throw new InvalidOperationException(Failure);
                }
                if ((attributes & FileAttributes.Directory) != NoEntries)
                {
                    pending.Push(path);
                    continue;
                }
                var identity = OfflineRegularFile.Inspect(path);
                if (!files.Add((identity.Device, identity.Inode)))
                {
                    throw new InvalidOperationException(Failure);
                }
                fileCount++;
            }
        }
        if (fileCount == NoEntries)
        {
            throw new InvalidOperationException(Failure);
        }
    }

    private static void RequireDirectory(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) == NoEntries
            || (attributes & FileAttributes.ReparsePoint) != NoEntries)
        {
            throw new InvalidOperationException(Failure);
        }
    }
}
