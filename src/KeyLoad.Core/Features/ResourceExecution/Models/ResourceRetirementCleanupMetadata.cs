using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

internal sealed record ResourceRetirementCleanupMetadata(ResourceRetirementCleanupCheckpointV1 Checkpoint,
    IReadOnlyList<StorageMutation> Mutations);
