using System.Collections.Immutable;
using KeyLoad.Clustering;

namespace KeyLoad.Server;

internal sealed partial class BackgroundStorageOperations
{
    public Task<BackgroundReadResult> ReadAnnProjectionWorkPageAsync(
        SiloAddress target, byte[]? after, BackgroundReadBudget budget, CancellationToken cancellationToken)
        => BackgroundStorageReadExecution.RunAsync(storage, clock, budget, (work, binding, token) =>
        {
            var page = Database.ReadAnnProjectionWorkPageBound(after, work, binding);
            return Task.FromResult(new AnnProjectionDiscoveryPage(page.Items.Select(item =>
                new AnnProjectionWorkerEvent(item.Consumer.Consumer, item.Consumer.Definition.IndexGeneration,
                    item.NodeId)).ToImmutableArray(), page.AfterKey is { } nextKey
                    ? ImmutableArray.CreateRange(nextKey) : null, page.Rejected));
        }, cancellationToken);

    public Task<BackgroundReadResult> PrepareAnnProjectionAsync(SiloAddress target,
        ProjectionConsumerRef consumer, long generation, Guid nodeId, AnnProjectionJobState? current,
        BackgroundReadBudget budget, CancellationToken cancellationToken)
        => BackgroundStorageReadExecution.RunAsync(storage, clock, budget,
            (work, binding, token) => new AnnProjectionStoragePlanning(Database, storage.Host.AnnMaintenance)
                .PrepareAsync(consumer, generation, nodeId, current, work, binding, token), cancellationToken);

    public Task<BackgroundReadResult> ReadAnnProjectionCreatorAsync(SiloAddress target,
        AnnProjectionJobState original, BackgroundReadBudget budget, CancellationToken cancellationToken)
        => BackgroundStorageReadExecution.RunAsync(storage, clock, budget, (work, binding, token) =>
        {
            var request = original.PendingRequest
                ?? throw Errors.Fail(ErrorCode.Validation, AnnProjectionJobProtocol.Invalid);
            var current = Database.ReadAnnProjectionWorkBound(request.Consumer, request.IndexGeneration, work, binding);
            AnnProjectionJobValidation.RequireCurrent(original, current);
            return Task.FromResult(current.Principal);
        }, cancellationToken);

    public Task<BackgroundReadResult> ValidateAnnProjectionResultAsync(SiloAddress target,
        AnnProjectionJobState original, AnnMaintenanceResult result, BackgroundReadBudget budget,
        CancellationToken cancellationToken)
        => BackgroundStorageReadExecution.RunAsync(storage, clock, budget, async (work, binding, token) =>
        {
            var request = original.PendingRequest
                ?? throw Errors.Fail(ErrorCode.Validation, AnnProjectionJobProtocol.Invalid);
            var current = Database.ReadAnnProjectionWorkBound(request.Consumer, request.IndexGeneration, work, binding);
            AnnProjectionJobValidation.RequireCurrent(original, current);
            var publication = await storage.Host.AnnMaintenance.ReadPublishedBoundAsync(current.Principal,
                request, work, binding, token).ConfigureAwait(true);
            AnnProjectionJobValidation.RequireResult(request, original.RequiredThroughSequence, result, publication);
            return true;
        }, cancellationToken);
}
