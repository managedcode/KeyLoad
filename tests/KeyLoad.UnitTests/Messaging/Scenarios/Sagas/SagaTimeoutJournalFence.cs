using KeyLoad.Clustering;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Orleans.DurableJobs;
using Orleans.Journaling;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SagaTimeoutJournalFence
{
    private const string JobRootSegment = "jobs";
    private const string JobShardsSegment = "shards";
    private const string RuntimeOwnerProperty = "DurableJobsOwner";

    internal static async Task<SagaJournalFence> ReadAsync(SagaTimeoutFixture fixture,
        DurableJob job, CancellationToken cancellationToken)
    {
        var services = fixture.SiloServices;
        // ADR-110 authorizes this execution journal fence API while it is experimental.
#pragma warning disable ORLEANSEXP005
        var journalId = JournalId.Create(JobRootSegment, JobShardsSegment, job.ShardId);
        var storage = services.GetRequiredKeyedService<IJournalStorageProvider>(RuntimeJournalRegistration.ProviderName)
            .CreateStorage(journalId);
        var metadata = await storage.GetMetadataAsync(cancellationToken);
        var header = await services.GetRequiredService<RuntimeJournalClient>()
            .GetHeaderAsync(journalId.Value, cancellationToken);
        var owner = metadata?.Properties.GetValueOrDefault(RuntimeOwnerProperty);
        var localSilo = services.GetRequiredService<ILocalSiloDetails>().SiloAddress.ToParsableString();
        if (metadata is null || header is null || string.IsNullOrWhiteSpace(owner)
            || !string.Equals(header.JournalName, journalId.Value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The execution durable job shard has no committed owner or journal fence.");
        }

        return new SagaJournalFence(header.JournalName, header.InstanceId, owner, localSilo,
            metadata.ETag ?? string.Empty, header.OwnerGeneration);
#pragma warning restore ORLEANSEXP005
    }
}
