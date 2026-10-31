using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.Query.Search;
using Microsoft.Extensions.Options;
using static KeyLoad.FunctionalTests.Search.AnnProviderQualificationProtocol;

namespace KeyLoad.FunctionalTests.Search;

internal static class AnnProviderQualificationPersistence
{
    internal static async Task SaveAsync(AnnProviderQualificationOwner owner, AnnProviderQualificationState state,
        PackedAnnIndex index, CancellationToken token)
    {
        var image = AnnProviderQualificationImages.Capture(owner);
        byte[] digest;
        using (var output = new FileStream(Path(owner, state), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            digest = index.Save(output, Options.Create(new PackedAnnStorageOptions()),
                AnnProviderQualificationCorpus.Budget(owner, token));
            await output.FlushAsync(token);
            RandomAccess.FlushToDisk(output.SafeFileHandle);
        }
        var bytes = await File.ReadAllBytesAsync(Path(owner, state), token);
        await Assert.That(SHA256.HashData(bytes).AsSpan().SequenceEqual(digest)).IsTrue();
        if (state.Healthy)
        {
            state.HealthyFile = bytes;
            state.HealthyDigest = digest;
        }
        else
        {
            state.OriginalFile = bytes;
            state.OriginalDigest = digest;
        }
        await FramesAsync(owner, state, index, bytes, token);
        await AnnProviderQualificationResults.CurrentAsync(owner, state, Load(owner, state, token), token);
        await AnnProviderQualificationImages.SameAsync(owner, image);
        var first = await File.ReadAllBytesAsync(owner.SnapshotPath, token);
        await Assert.That(first.AsSpan().SequenceEqual(state.OriginalFile)).IsTrue();
    }

    internal static PackedAnnIndex Load(AnnProviderQualificationOwner owner, AnnProviderQualificationState state,
        CancellationToken token)
    {
        using var source = new FileStream(Path(owner, state), FileMode.Open, FileAccess.Read, FileShare.Read);
        return PackedAnnIndex.Load(source, state.Healthy ? state.HealthyDigest : state.OriginalDigest, UnitExecutionOptions.PackedAnn(state.Policy),
            Options.Create(new PackedAnnStorageOptions()), AnnProviderQualificationCorpus.Budget(owner, token));
    }

    internal static async Task WriteExactAsync(AnnProviderQualificationOwner owner, byte[] bytes, CancellationToken token)
    {
        using var output = new FileStream(owner.SnapshotPath, FileMode.Truncate, FileAccess.Write, FileShare.None);
        await output.WriteAsync(bytes, token);
        await output.FlushAsync(token);
        RandomAccess.FlushToDisk(output.SafeFileHandle);
    }

    internal static string Path(AnnProviderQualificationOwner owner, AnnProviderQualificationState state)
        => state.Healthy ? System.IO.Path.Combine(owner.Source.Directory, HealthySnapshotFile) : owner.SnapshotPath;

    private static async Task FramesAsync(AnnProviderQualificationOwner owner, AnnProviderQualificationState state,
        PackedAnnIndex index, byte[] bytes, CancellationToken token)
    {
        using var source = new MemoryStream(bytes, writable: false);
        var budget = AnnProviderQualificationCorpus.Budget(owner, token);
        var header = PackedAnnStorageFrames.Read<PackedAnnStorageHeader>(source, budget);
        await Assert.That(header.Version).IsEqualTo(PackedAnnStorageFrames.CurrentVersion);
        await Assert.That(header.Space).IsEqualTo(AnnProviderQualificationCorpus.Space(state.Metric));
        await Assert.That(header.Policy).IsEqualTo(state.Policy);
        await Assert.That(header.Count).IsEqualTo(CorpusCount);
        await Assert.That(header.RetainedBytes).IsEqualTo(index.RetainedBytesUpperBound
            - PackedAnnReservations.CosineNorms(header.Space.Metric, header.Count));
        await Assert.That(header.BuildScratchBytes).IsEqualTo(index.BuildScratchBytesUpperBound);
        var records = AnnProviderQualificationCorpus.Expected(state);
        var nodes = new PackedAnnStorageNode[header.Count];
        for (var ordinal = FirstNode; ordinal < nodes.Length; ordinal++)
        {
            var node = nodes[ordinal] = PackedAnnStorageFrames.Read<PackedAnnStorageNode>(source, budget);
            await Assert.That(node.Ordinal).IsEqualTo(ordinal);
            await Assert.That(node.Id).IsEqualTo(records[ordinal].DocumentId);
            await Assert.That(node.Revision).IsEqualTo(records[ordinal].DocumentRevision);
            await Assert.That(JsonSerializer.Serialize(node.Vector, JsonDefaults.Options))
                .IsEqualTo(JsonSerializer.Serialize(records[ordinal].Values, JsonDefaults.Options));
            PackedAnnStorageValidation.Node(node, header, ordinal,
                ordinal == FirstNode ? null : records[ordinal - OrdinalStep].DocumentId, budget);
        }
        await Assert.That(source.Position).IsEqualTo(source.Length);
        PackedAnnStorageValidation.Topology(header, nodes, budget);
    }
}
