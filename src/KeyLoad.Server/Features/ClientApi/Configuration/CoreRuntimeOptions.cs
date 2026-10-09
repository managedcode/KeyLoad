using KeyLoad.Core;
using KeyLoad.Query;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Shares validated database, due-work, event and query policies with execution owners.</summary>
internal sealed record CoreRuntimeOptions(
    IOptions<SerializationExecutionOptions> Serialization,
    IOptions<DatabaseLimits> DatabaseLimits,
    IOptions<DueWorkExecutionOptions> DueWork,
    IOptions<EventSourceExecutionOptions> EventSource,
    IOptions<MessagingExecutionOptions> Messaging,
    IOptions<GraphExecutionOptions> GraphExecution,
    IOptions<ChangeFeedExecutionOptions> ChangeFeedExecution,
    IOptions<BlobExecutionOptions> BlobExecution,
    IOptions<NativeClaimsExecutionOptions> NativeClaimsExecution,
    IOptions<TimeSeriesExecutionOptions> TimeSeriesExecution,
    IOptions<PartitionMovementCheckpointOptions> MovementCheckpoints,
    IOptions<KeyLoad.Query.Features.Search.PackedAnnOptions> PackedAnn,
    IOptions<KeyLoad.Core.Features.Search.AnnSeedOptions> AnnSeed,
    IOptions<KeyLoad.Query.Features.Search.PackedAnnStorageOptions> PackedAnnStorage,
    IOptions<AnnMaintenanceOptions> AnnMaintenance,
    IOptions<TextIndexMaintenanceOptions> TextMaintenance,
    IOptions<QueryExecutionOptions> QueryExecution,
    IOptions<CacheMemoryLimits> CacheMemory,
    IOptions<CacheReadPermitOptions> CacheReadPermit,
    IOptions<RuntimeJournalOptions> RuntimeJournal,
    IOptions<CommandInboxExecutionOptions> CommandInbox)
{
    internal void ValidateBeforePhysicalOwnership()
    {
        _ = Serialization.Value;
        _ = DatabaseLimits.Value;
        _ = DueWork.Value;
        _ = EventSource.Value;
        _ = Messaging.Value;
        _ = GraphExecution.Value;
        _ = ChangeFeedExecution.Value;
        _ = BlobExecution.Value;
        _ = NativeClaimsExecution.Value;
        _ = TimeSeriesExecution.Value;
        _ = MovementCheckpoints.Value;
        _ = PackedAnn.Value;
        _ = AnnSeed.Value;
        _ = PackedAnnStorage.Value;
        _ = AnnMaintenance.Value;
        _ = TextMaintenance.Value;
        _ = QueryExecution.Value;
        _ = CacheMemory.Value;
        _ = CacheReadPermit.Value;
        _ = RuntimeJournal.Value;
        _ = CommandInbox.Value;
    }

    internal void RegisterBorrowed(IServiceCollection services)
    {
        services.AddSingleton(Serialization);
        services.AddSingleton(DatabaseLimits);
        services.AddSingleton(DueWork);
        services.AddSingleton(EventSource);
        services.AddSingleton(Messaging);
        services.AddSingleton(GraphExecution);
        services.AddSingleton(ChangeFeedExecution);
        services.AddSingleton(BlobExecution);
        services.AddSingleton(NativeClaimsExecution);
        services.AddSingleton(TimeSeriesExecution);
        services.AddSingleton(MovementCheckpoints);
        services.AddSingleton(PackedAnn);
        services.AddSingleton(AnnSeed);
        services.AddSingleton(PackedAnnStorage);
        services.AddSingleton(AnnMaintenance);
        services.AddSingleton(TextMaintenance);
        services.AddSingleton(QueryExecution);
        services.AddSingleton(CacheMemory);
        services.AddSingleton(CacheReadPermit);
        services.AddSingleton(RuntimeJournal);
        services.AddSingleton(CommandInbox);
    }
}
