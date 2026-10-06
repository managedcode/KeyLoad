using KeyLoad.Orleans;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Orleans.DurableJobs;
using Orleans.Journaling;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class NativeSagaTimeoutJournalFence
{
    private const string JobRootSegment = "jobs";
    private const string JobShardsSegment = "shards";
    private const string NativeOwnerProperty = "DurableJobsOwner";

    internal static async Task<NativeSagaJournalFence> ReadAsync(NativeSagaTimeoutFixture fixture,
        DurableJob job, CancellationToken cancellationToken)
    {
        var services = fixture.SiloServices;
        // ADR-110 authorizes this native journal fence API while it is experimental.
#pragma warning disable ORLEANSEXP005
        var journalId = JournalId.Create(JobRootSegment, JobShardsSegment, job.ShardId);
        var storage = services.GetRequiredKeyedService<IJournalStorageProvider>(NativeRuntimeJournalRegistration.ProviderName)
            .CreateStorage(journalId);
        var metadata = await storage.GetMetadataAsync(cancellationToken);
        var header = await services.GetRequiredService<RuntimeJournalClient>()
            .GetHeaderAsync(journalId.Value, cancellationToken);
        var owner = metadata?.Properties.GetValueOrDefault(NativeOwnerProperty);
        var localSilo = services.GetRequiredService<ILocalSiloDetails>().SiloAddress.ToParsableString();
        if (metadata is null || header is null || string.IsNullOrWhiteSpace(owner)
            || !string.Equals(header.JournalName, journalId.Value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The native durable job shard has no committed owner or journal fence.");
        }

        return new NativeSagaJournalFence(header.JournalName, header.InstanceId, owner, localSilo,
            metadata.ETag ?? string.Empty, header.OwnerGeneration);
#pragma warning restore ORLEANSEXP005
    }
}

internal sealed record NativeSagaJournalFence(string JournalName, Guid InstanceId, string NativeOwner,
    string LocalSilo, string NativeMetadataETag, long CanonicalOwnerGeneration);
