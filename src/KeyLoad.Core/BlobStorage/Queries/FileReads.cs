using System.Collections.Immutable;
using KeyLoad.Core.BlobStorage;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes FileReads operations against the caller's database context.</summary>
public static class FileReads
{
    internal static FileMetadataState ReadFileMetadataBound(this DatabaseEngine engine, string principalId, FileMetadataQuery request, IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken)
    {
        var work = new ReadExecutionBudget(engine.EvaluationClock, cancellationToken);
        work.AttachResourceLifetimeBinding(binding);
        using var admission = engine.AdmitQuery(cancellationToken);
        var result = engine.Store.Read(raw => engine.ReadFileMetadata(work.CreateView(raw), principalId, request));
        return result;
    }

    internal static FileMetadataState ReadFileMetadata(this DatabaseEngine engine, string principalId, FileMetadataQuery request) => engine.Store.Read(view => engine.ReadFileMetadata(view, principalId, request));
    internal static FileMetadataState ReadFileMetadata(this DatabaseEngine engine, IKeyValueView view, string principalId, FileMetadataQuery request)
    {
        FileMetadataKeys.Validate(request.Address, allowEmpty: true);
        if (request.Capability is not (Capability.BlobRead or Capability.BlobWrite or Capability.BlobDelete or Capability.BlobManage))
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, FileStorageProtocol.Denied);
        }

        var principal = engine.Principal(view, principalId, engine.Clock.GetUtcNow());
        engine.Authorization.Require(principal, request.Address.Partition, request.Address.Resource, request.Capability);
        _ = ResourceLifetimeReadBinding.BindAuthorizedReadResource(view, request.Address.Partition, request.Address.Resource, ResourceKind.BlobStore);
        var pair = ResourceDefinitionBinding.ReadBoundResourcePair(view, request.Address.Partition, request.Address.Resource, ResourceKind.BlobStore);
        var incarnation = pair.Lifetime.Incarnation;
        var head = request.Address.Path.Length == FileStorageProtocol.EmptyCount ? null : view.GetRecord<FileEntry>(FileMetadataKeys.Head(request.Address, incarnation));
        if (head is not null && !engine.Authorization.CanReadRow(principal, head.Access))
        {
            throw Errors.Fail(ErrorCode.NotFound, FileStorageProtocol.Denied);
        }

        var upload = request.UploadId == Guid.Empty ? null : view.GetRecord<FileUpload>(FileMetadataKeys.Upload(request.Address, incarnation, request.UploadId));
        if (upload is not null && (!engine.Authorization.CanReadRow(principal, upload.Access) || (upload.Address.Partition != request.Address.Partition || upload.Address.Resource != request.Address.Resource || request.Address.Path.Length != FileStorageProtocol.EmptyCount && upload.Address.Path != request.Address.Path) || upload.Creator != principalId && request.Capability != Capability.BlobManage && !principal.ClusterAdministrator))
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, FileStorageProtocol.Denied);
        }

        return request.List ? engine.ReadFilePage(view, principal, request, incarnation) : new(incarnation, head, upload, [], Uploads: []);
    }

    internal static FileMetadataState ReadFilePage(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, FileMetadataQuery request, ResourceIncarnation incarnation)
    {
        if (request.Limit < FileStorageProtocol.FirstRecordCount || view.MaximumRangeRecords < FileStorageProtocol.FirstRecordCount)
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
        var after = request.After is null ? null : uploads ? FileMetadataKeys.Upload(request.Address, incarnation, afterUpload) : FileMetadataKeys.Head(request.Address with { Path = request.After }, incarnation);
        var page = view.Scan(prefix, Math.Min(request.Limit, view.MaximumRangeRecords), after);
        var entries = ImmutableArray.CreateBuilder<FileEntry>();
        var leases = ImmutableArray.CreateBuilder<FileUpload>();
        string? next = null;
        foreach (var record in page.Records)
        {
            if (uploads)
            {
                var lease = OrleansSerialization.Deserialize<FileUpload>(record.Value.Span);
                if (engine.Authorization.CanReadRow(principal, lease.Access))
                {
                    leases.Add(lease);
                }

                next = lease.UploadId.ToString(FileStorageProtocol.GuidFormat);
            }
            else
            {
                var entry = OrleansSerialization.Deserialize<FileEntry>(record.Value.Span);
                next = entry.Address.Path;
                if (!entry.Deleted && engine.Authorization.CanReadRow(principal, entry.Access))
                {
                    entries.Add(entry);
                }
            }
        }

        return new(incarnation, null, null, entries.ToImmutable(), page.HasMore, leases.ToImmutable(), next);
    }
}
