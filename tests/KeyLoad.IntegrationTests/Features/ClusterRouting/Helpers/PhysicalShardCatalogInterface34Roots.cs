using System.Security.Cryptography;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.StorageRecovery;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record PhysicalShardCatalogInterface34Roots(string Root, string Baseline, string Upgrade,
    string Mixed, string RollbackSource, string RollbackRun)
{
    private const string RootPrefix = "cluster-routing-interface34-";

    internal static PhysicalShardCatalogInterface34Roots Create()
    {
        var artifactDirectory = Path.Combine(ClusterFixtureDiagnostics.FindRepositoryRoot().FullName,
            "artifacts", "qualification");
        Directory.CreateDirectory(artifactDirectory);
        var root = Path.Combine(artifactDirectory, RootPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
        return new(root, Path.Combine(root, "baseline"), Path.Combine(root, "upgrade"),
            Path.Combine(root, "mixed"), Path.Combine(root, "rollback-source"), Path.Combine(root, "rollback-run"));
    }

    internal static async Task<NodeEpochRf3Inventory[]> CaptureAsync(string root, CancellationToken cancellationToken)
    {
        var inventories = new NodeEpochRf3Inventory[RequestCqrsRf3Protocol.NodeCount];
        for (var index = 0; index < inventories.Length; index++)
        {
            var nodeRoot = Path.Combine(root, RequestCqrsRf3Protocol.NodeName(index));
            inventories[index] = await NodeEpochRf3Inventory.CaptureAsync(nodeRoot, cancellationToken)
                .ConfigureAwait(false);
        }
        return inventories;
    }

    internal async Task CreatePreUpgradeCopiesAsync(PhysicalShardCatalogInterface34Baseline baseline,
        CancellationToken cancellationToken)
    {
        await CopyRootAsync(Baseline, Upgrade, baseline, cancellationToken).ConfigureAwait(false);
        await CopyRootAsync(Baseline, Mixed, baseline, cancellationToken).ConfigureAwait(false);
        await CopyRootAsync(Baseline, RollbackSource, baseline, cancellationToken).ConfigureAwait(false);
    }

    internal async Task RestoreRollbackCopyAsync(PhysicalShardCatalogInterface34Baseline baseline,
        CancellationToken cancellationToken)
        => await CopyRootAsync(RollbackSource, RollbackRun, baseline, cancellationToken).ConfigureAwait(false);

    internal async Task AssertImmutableSourcesAsync(PhysicalShardCatalogInterface34Baseline baseline,
        NodeEpochRf3Inventory[] rollbackSourceInventories, CancellationToken cancellationToken)
    {
        await AssertRootMatchesAsync(Baseline, baseline, cancellationToken).ConfigureAwait(false);
        await AssertRootMatchesAsync(RollbackSource, baseline with { NodeInventories = rollbackSourceInventories },
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task CopyRootAsync(string sourceRoot, string destinationRoot,
        PhysicalShardCatalogInterface34Baseline baseline, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destinationRoot);
        for (var index = 0; index < baseline.NodeInventories.Length; index++)
        {
            var node = RequestCqrsRf3Protocol.NodeName(index);
            var copied = await NodeEpochRf3Inventory.CopyTreeAsync(Path.Combine(sourceRoot, node),
                Path.Combine(destinationRoot, node), cancellationToken).ConfigureAwait(false);
            await Assert.That(copied.Equivalent(baseline.NodeInventories[index])).IsTrue();
        }
        var profileSha = await baseline.Profile.CopyExactAsync(destinationRoot, baseline.ProfileBytes,
            cancellationToken).ConfigureAwait(false);
        await Assert.That(profileSha).IsEqualTo(Convert.ToHexStringLower(SHA256.HashData(baseline.ProfileBytes)));
    }

    private static async Task AssertRootMatchesAsync(string root,
        PhysicalShardCatalogInterface34Baseline baseline, CancellationToken cancellationToken)
    {
        for (var index = 0; index < baseline.NodeInventories.Length; index++)
        {
            var node = RequestCqrsRf3Protocol.NodeName(index);
            var actual = await NodeEpochRf3Inventory.CaptureAsync(Path.Combine(root, node), cancellationToken)
                .ConfigureAwait(false);
            await Assert.That(actual.Equivalent(baseline.NodeInventories[index])).IsTrue();
        }
        var profile = await NodeEpochRf3Profile.ReadAsync(Path.Combine(root, NodeEpochRf3Protocol.ProfileFile),
            cancellationToken).ConfigureAwait(false);
        await Assert.That(profile.Bytes.AsSpan().SequenceEqual(baseline.ProfileBytes)).IsTrue();
    }
}
