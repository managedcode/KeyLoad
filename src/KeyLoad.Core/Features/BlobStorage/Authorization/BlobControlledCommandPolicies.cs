using System.Collections.Immutable;
using KeyLoad.Core.Features.BlobStorage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private BlobCommandScope RequireControlledBlobPolicies(PrincipalRecord principal, ReplicatedOperation original,
        ImmutableArray<ResourceDefinition> resources)
    {
        ClusterPrincipalPolicy.RequireOperation(principal, original.Kind);
        ClusterPrincipalPolicy.RequireOperation(principal, OperationKind.BeginBlobUpload);
        var scope = BlobCommandScope.From(original);
        BlobKeys.Validate(scope.Blob);
        if (scope.CommandId != original.Id || scope.UploadId == Guid.Empty)
        { throw BlobErrors.Validation(); }
        Authorization.Require(principal, scope.Blob.Partition, scope.Blob.Resource,
            BlobCommandScope.RequiredCapability(original.Kind));
        if (resources.IsDefault)
        { throw BlobErrors.Corruption(); }
        var resource = resources.SingleOrDefault(value => value.Name == scope.Blob.Resource)
            ?? throw Errors.Fail(ErrorCode.NotFound, DatabaseEngineResourceIsNotConfiguredDetail);
        if (resource.Kind != ResourceKind.BlobStore
            || resource.TransactionDomainId != scope.Blob.Partition.TransactionDomainId)
        { throw BlobErrors.Validation(); }
        BlobQuotaOperations.ValidatePolicy(resource);
        if (original.Kind == OperationKind.BeginBlobUpload
            && BlobCommandScope.Payload<BeginBlobUploadRequest>(original).Access is { } access)
        {
            BlobMetadataRules.Access(access, false);
            Authorization.RequireWriteRow(principal, access);
        }
        return scope;
    }
}
