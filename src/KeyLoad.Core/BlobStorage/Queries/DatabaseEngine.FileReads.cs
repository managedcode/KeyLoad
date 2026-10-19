using System.Collections.Immutable;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal FileMetadataState ReadFileMetadataBound(string principalId, FileMetadataQuery request,
        IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken)
    {
        var work = new ReadExecutionBudget(OperationLimitsOptions, EvaluationClock, cancellationToken);
        work.AttachResourceLifetimeBinding(binding);
        using var admission = AdmitQuery(cancellationToken);
        var result = Store.Read(raw => ReadFileMetadata(work.CreateView(raw), principalId, request));
        work.CheckResult(result);
        return result;
    }

    internal FileMetadataState ReadFileMetadata(string principalId, FileMetadataQuery request)
        => Store.Read(view => ReadFileMetadata(view, principalId, request));

    private FileMetadataState ReadFileMetadata(IKeyValueView view, string principalId, FileMetadataQuery request)
    {
        FileMetadataKeys.Validate(request.Address, allowEmpty: true);
        if (request.Capability is not (Capability.BlobRead or Capability.BlobWrite or Capability.BlobDelete or Capability.BlobManage))
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, FileStorageProtocol.Denied);
        }
        var principal = Principal(view, principalId, Clock.GetUtcNow());
        Authorization.Require(principal, request.Address.Partition, request.Address.Resource, request.Capability);
        _ = BindAuthorizedReadResource(view, request.Address.Partition, request.Address.Resource, ResourceKind.BlobStore);
        var pair = ReadBoundResourcePair(view, request.Address.Partition, request.Address.Resource, ResourceKind.BlobStore);
        var incarnation = pair.Lifetime.Incarnation;
        var head = request.Address.Path.Length == FileStorageProtocol.EmptyCount ? null
            : view.GetRecord<FileEntry>(FileMetadataKeys.Head(request.Address, incarnation));
        if (head is not null && !Authorization.CanReadRow(principal, head.Access))
        {
            throw Errors.Fail(ErrorCode.NotFound, FileStorageProtocol.Denied);
        }
        var upload = request.UploadId == Guid.Empty ? null
            : view.GetRecord<FileUpload>(FileMetadataKeys.Upload(request.Address, incarnation, request.UploadId));
        if (upload is not null && (!Authorization.CanReadRow(principal, upload.Access) || (upload.Address.Partition != request.Address.Partition || upload.Address.Resource != request.Address.Resource
            || request.Address.Path.Length != FileStorageProtocol.EmptyCount && upload.Address.Path != request.Address.Path) || upload.Creator != principalId && request.Capability != Capability.BlobManage && !principal.ClusterAdministrator))
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, FileStorageProtocol.Denied);
        }
        return request.List ? ReadFilePage(view, principal, request, incarnation)
            : new(incarnation, head, upload, [], Uploads: []);
    }

    private FileMetadataState ReadFilePage(IKeyValueView view, PrincipalRecord principal,
        FileMetadataQuery request, ResourceIncarnation incarnation)
    {
        if (request.Limit < FileStorageProtocol.FirstRecordCount || request.Limit > Limits.MaxResults || view.MaximumRangeRecords < FileStorageProtocol.FirstRecordCount)
        {
            throw Errors.Fail(ErrorCode.Validation, FileStorageProtocol.Invalid);
        }
        var uploads = request.Capability == Capability.BlobManage;
        var afterUpload = Guid.Empty;
        if (request.After is not null)
        {
            if (uploads && (!Guid.TryParseExact(request.After, FileStorageProtocol.GuidFormat, out afterUpload) || afterUpload == Guid.Empty))
            {
                throw Errors.Fail(ErrorCode.Validation, FileStorageProtocol.Invalid);
            }
            if (!uploads)
            {
                FileMetadataKeys.Validate(request.Address with { Path = request.After });
            }
        }
        var family = uploads ? FileStorageProtocol.UploadFamily : FileStorageProtocol.HeadFamily;
        var prefix = FileMetadataKeys.Prefix(request.Address, incarnation, family);
        var after = request.After is null ? null : uploads
            ? FileMetadataKeys.Upload(request.Address, incarnation, afterUpload)
            : FileMetadataKeys.Head(request.Address with { Path = request.After }, incarnation);
        var page = view.Scan(prefix, Math.Min(request.Limit, view.MaximumRangeRecords), after);
        var entries = ImmutableArray.CreateBuilder<FileEntry>();
        var leases = ImmutableArray.CreateBuilder<FileUpload>();
        string? next = null;
        foreach (var record in page.Records)
        {
            if (uploads)
            {
                var lease = OrleansSerialization.Deserialize<FileUpload>(record.Value.Span);
                if (Authorization.CanReadRow(principal, lease.Access))
                {
                    leases.Add(lease);
                }
                next = lease.UploadId.ToString(FileStorageProtocol.GuidFormat);
            }
            else
            {
                var entry = OrleansSerialization.Deserialize<FileEntry>(record.Value.Span);
                next = entry.Address.Path;
                if (!entry.Deleted && Authorization.CanReadRow(principal, entry.Access))
                {
                    entries.Add(entry);
                }
            }
        }
        return new(incarnation, null, null, entries.ToImmutable(), page.HasMore, leases.ToImmutable(), next);
    }
}
