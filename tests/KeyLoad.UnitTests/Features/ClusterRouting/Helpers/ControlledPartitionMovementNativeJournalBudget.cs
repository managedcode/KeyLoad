#pragma warning disable ORLEANSEXP005
using System.Buffers;
using KeyLoad.Clustering;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Uses the production native journal provider at eight KiB without changing its storage capacity settings.</summary>
internal static class ControlledPartitionMovementNativeJournalBudget
{
    private const string JournalName = "native/hosted-eight-kib-budget";
    private const string ScenarioProperty = "scenario";
    private const string ScenarioValue = "hosted-eight-kib-budget";
    private const int HealthyBodyBytes = 1024;
    private const int FirstPatternIndex = 0;

    internal static async Task<RuntimeJournalSnapshot> CreateAndRefuseAsync(ControlledPartitionMovementNativeHost owner,
        CancellationToken token)
    {
        var storage = Storage(owner);
        await Assert.That(await storage.CreateIfNotExistsAsync(new Dictionary<string, string>
        {
            [ScenarioProperty] = ScenarioValue
        }, token)).IsTrue();
        var content = Content();
        await storage.AppendAsync(new ReadOnlySequence<byte>(content), token);
        var before = await HeaderAsync(owner, token);
        var position = owner.Store.Position;
        var index = owner.Log.State.LastIndex;
        var image = ControlledPartitionMovementRawImage.Bytes(owner.Store);
        var oversized = new byte[owner.Database.Limits.MaxBatchBytes];
        var refused = RuntimeJournalStorageOperations.BodyMutation(RuntimeJournalAction.Replace, before, oversized);
        await Assert.That(NativeSerialization.Measure(refused)).IsGreaterThan(owner.Database.Limits.MaxBatchBytes);
        var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () =>
            await storage.ReplaceAsync(new ReadOnlySequence<byte>(oversized), token));
        await Assert.That(failure!.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(owner.Store.Position).IsEqualTo(position);
        await Assert.That(owner.Log.State.LastIndex).IsEqualTo(index);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(owner.Store).SequenceEqual(image)).IsTrue();
        await VerifyAsync(owner, before, token);
        var healthy = RuntimeJournalStorageOperations.BodyMutation(RuntimeJournalAction.Replace, before, content);
        await Assert.That(NativeSerialization.Measure(healthy)).IsLessThanOrEqualTo(owner.Database.Limits.MaxBatchBytes);
        await storage.ReplaceAsync(new ReadOnlySequence<byte>(content), token);
        var current = await HeaderAsync(owner, token);
        await Assert.That(current.InstanceId).IsEqualTo(before.InstanceId);
        await Assert.That(current.ContentRevision).IsGreaterThan(before.ContentRevision);
        await VerifyAsync(owner, current, token);
        return current;
    }

    internal static async Task VerifyAsync(ControlledPartitionMovementNativeHost owner,
        RuntimeJournalSnapshot expected, CancellationToken token)
    {
        var actual = await HeaderAsync(owner, token);
        await Assert.That(NativeSerialization.Serialize(actual).SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
        var storage = Storage(owner);
        using var replay = new RuntimeJournalNativeReadAccumulator();
        await storage.ReadAsync(replay, token);
        await Assert.That(replay.IsCompleted).IsTrue();
        await Assert.That(replay.ToArray().SequenceEqual(Content())).IsTrue();
        await Assert.That(replay.Metadata?.FormatKey).IsEqualTo(RuntimeJournalStoragePolicy.BinaryFormat);
        await Assert.That(replay.Metadata?.ETag).IsEqualTo(expected.MetadataETag);
        await Assert.That(replay.Metadata!.Properties[ScenarioProperty]).IsEqualTo(ScenarioValue);
        var catalog = owner.Application.Services.GetRequiredKeyedService<IJournalStorageCatalog>(NativeRuntimeJournalRegistration.ProviderName);
        var entries = new List<JournalCatalogEntry>();
        await foreach (var entry in catalog.ListAsync(new JournalCatalogListOptions
        {
            Prefix = new(JournalName),
            MinId = new(JournalName),
            MaxId = new(JournalName),
            IncludeMetadata = true
        }, token))
        {
            entries.Add(entry);
        }
        await Assert.That(entries.Select(entry => entry.Id.Value)).IsEquivalentTo(new[] { JournalName });
        await Assert.That(entries.Single().Metadata?.ETag).IsEqualTo(expected.MetadataETag);
    }

    internal static async Task DeleteAsync(ControlledPartitionMovementNativeHost owner, CancellationToken token)
    {
        var storage = Storage(owner);
        await storage.DeleteAsync(token);
        await Assert.That(await storage.GetMetadataAsync(token)).IsNull();
    }

    private static IJournalStorage Storage(ControlledPartitionMovementNativeHost owner)
        => owner.Application.Services.GetRequiredKeyedService<IJournalStorageProvider>(NativeRuntimeJournalRegistration.ProviderName)
            .CreateStorage(new(JournalName));

    private static async Task<RuntimeJournalSnapshot> HeaderAsync(ControlledPartitionMovementNativeHost owner, CancellationToken token)
        => await owner.Application.Services.GetRequiredService<RuntimeJournalClient>().GetHeaderAsync(JournalName, token)
            ?? throw new InvalidOperationException(nameof(RuntimeJournalSnapshot));

    private static byte[] Content()
        => Enumerable.Range(FirstPatternIndex, HealthyBodyBytes).Select(index => (byte)(index % byte.MaxValue)).ToArray();
}
#pragma warning restore ORLEANSEXP005
