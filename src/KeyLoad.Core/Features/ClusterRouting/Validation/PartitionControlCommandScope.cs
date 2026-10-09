using System.Collections.Immutable;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private CommandRequest? RequireControlledCommandBody(ReplicatedOperation original, PartitionRef partition)
    {
        if (original.Kind == OperationKind.Batch)
        { return RequireControlledDocumentBatch(original, partition); }
        if (!BlobStorageOperations.Handles(original.Kind))
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, UnsupportedOperationMessage); }
        var scope = BlobCommandScope.From(original);
        BlobKeys.Validate(scope.Blob);
        if (scope.CommandId != original.Id || scope.Blob.Partition != partition || scope.UploadId == Guid.Empty)
        { throw Errors.Fail(ErrorCode.Validation, PartitionMoveProtocol.Invalid); }
        return null;
    }

    private void RequireControlledCommandPolicies(PrincipalRecord principal, ReplicatedOperation original,
        PartitionRef partition, ImmutableArray<ResourceDefinition> resources)
    {
        var command = RequireControlledCommandBody(original, partition);
        if (command is not null)
        { RequireControlledDocumentPolicies(principal, command, resources); return; }
        _ = RequireControlledBlobPolicies(principal, original, resources);
    }
}
