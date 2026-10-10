namespace KeyLoad.Core.Features.InternalSerialization;

internal static class AuthorizedDocumentChangeFields
{
    internal const uint Sequence = 0;
    internal const uint Commit = 1;
    internal const uint CommittedAt = 2;
    internal const uint Before = 3;
    internal const uint After = 4;
}

internal static class AuthorizedDocumentChangePageFields
{
    internal const uint Changes = 0;
    internal const uint Cursor = 1;
    internal const uint ThroughSequence = 2;
    internal const uint Tail = 3;
    internal const uint FirstAvailable = 4;
    internal const uint HasMore = 5;
    internal const uint CutPosition = 6;
}

internal static class BlobCommandScopeFields
{
    internal const uint CommandId = 0;
    internal const uint Blob = 1;
    internal const uint UploadId = 2;
}

internal static class BlobHeadFields
{
    internal const uint FormatVersion = 0;
    internal const uint Incarnation = 1;
    internal const uint Metadata = 2;
}

internal static class BlobOutcomeAuthorityFields
{
    internal const uint FormatVersion = 0;
    internal const uint BeginCommandId = 1;
    internal const uint CreatorPrincipalId = 2;
}

internal static class BlobPartMetadataFields
{
    internal const uint FormatVersion = 0;
    internal const uint Length = 1;
    internal const uint Sha256 = 2;
}

internal static class BlobQuotaFields
{
    internal const uint FormatVersion = 0;
    internal const uint Incarnation = 1;
    internal const uint ReservedBytes = 2;
    internal const uint ObjectKeys = 3;
    internal const uint Versions = 4;
    internal const uint Uploads = 5;
}

internal static class BlobRestoreMarkerFields
{
    internal const uint FormatVersion = 0;
    internal const uint SourceIncarnation = 1;
    internal const uint TargetIncarnation = 2;
    internal const uint Phase = 3;
    internal const uint ExclusiveCursor = 4;
    internal const uint ReservedBytes = 5;
    internal const uint ObjectKeys = 6;
    internal const uint Versions = 7;
    internal const uint ActiveUploads = 8;
    internal const uint Resources = 9;
}

internal static class BlobStateFields
{
    internal const uint FormatVersion = 0;
    internal const uint Incarnation = 1;
    internal const uint IntegrityIncarnation = 2;
    internal const uint BeginCommandId = 3;
    internal const uint CreatorPrincipalId = 4;
    internal const uint Blob = 5;
    internal const uint UploadId = 6;
    internal const uint Access = 7;
    internal const uint DeclaredLength = 8;
    internal const uint ExpectedRevision = 9;
    internal const uint ExpiresAt = 10;
    internal const uint NextOrdinal = 11;
    internal const uint StoredBytes = 12;
    internal const uint IntegrityHash = 13;
    internal const uint Status = 14;
    internal const uint RemainingReservation = 15;
    internal const uint ReclaimCursor = 16;
    internal const uint Retired = 17;
}

internal static class DocumentMutationContextFields
{
    internal const uint Key = 0;
    internal const uint Before = 1;
}

internal static class DocumentMutationResultFields
{
    internal const uint Receipt = 0;
    internal const uint After = 1;
}

internal static class EventIdentityFields
{
    internal const uint StreamId = 0;
    internal const uint Generation = 1;
    internal const uint Revision = 2;
}

internal static class GroupClaimsFields
{
    internal const uint Purpose = 0;
    internal const uint Incarnation = 1;
    internal const uint Subscription = 2;
    internal const uint Generation = 3;
    internal const uint OwnershipEpoch = 4;
    internal const uint Position = 5;
    internal const uint LeaseVersion = 6;
    internal const uint PrincipalId = 7;
    internal const uint PolicyEpoch = 8;
    internal const uint DataPolicyEpoch = 9;
}

internal static class GroupDeliveryFields
{
    internal const uint Position = 0;
    internal const uint State = 1;
    internal const uint Attempts = 2;
    internal const uint LeaseVersion = 3;
    internal const uint AvailableAt = 4;
    internal const uint LeaseUntil = 5;
    internal const uint PrincipalId = 6;
}

internal static class GroupStateFields
{
    internal const uint Definition = 0;
    internal const uint Generation = 1;
    internal const uint OwnershipEpoch = 2;
    internal const uint Checkpoint = 3;
    internal const uint IssuedPosition = 4;
    internal const uint Paused = 5;
    internal const uint SafeFailureCode = 6;
}

internal static class InboxRecordFields
{
    internal const uint Fingerprint = 0;
    internal const uint Receipt = 1;
}

internal static class MembershipMutationFields
{
    internal const uint Key = 0;
    internal const uint ExpectedVersion = 1;
    internal const uint Payload = 2;
}

internal static class MembershipRecordFields
{
    internal const uint Version = 0;
    internal const uint Payload = 1;
}

internal static class ProjectionBatchClaimsFields
{
    internal const uint Purpose = 0;
    internal const uint Incarnation = 1;
    internal const uint Consumer = 2;
    internal const uint IndexGeneration = 3;
    internal const uint After = 4;
    internal const uint Through = 5;
    internal const uint ExpiresAt = 6;
}

internal static class ProjectionReceiptFields
{
    internal const uint Fingerprint = 0;
    internal const uint Receipt = 1;
    internal const uint Checkpoint = 2;
}

internal static class ReadyClaimInputFields
{
    internal const uint ReadyKey = 0;
    internal const uint MetadataKey = 1;
    internal const uint Metadata = 2;
    internal const uint TransitionKey = 3;
    internal const uint DeletesBody = 4;
}

internal static class SampleReadScopeFields
{
    internal const uint Principal = 0;
    internal const uint Resource = 1;
    internal const uint Prefix = 2;
}

internal static class SourceCursorFields
{
    internal const uint Purpose = 0;
    internal const uint Incarnation = 1;
    internal const uint Source = 2;
    internal const uint PrincipalId = 3;
    internal const uint PolicyEpoch = 4;
    internal const uint SchemaVersion = 5;
    internal const uint Position = 6;
    internal const uint ExpiresAt = 7;
}

internal static class StoredMessageBodyFields
{
    internal const uint Body = 0;
    internal const uint Bytes = 1;
}

internal static class StoredOutboxEntryFields
{
    internal const uint Entry = 0;
    internal const uint Key = 1;
    internal const uint StoredBytes = 2;
}

internal static class StoredOutcomeFields
{
    internal const uint Fingerprint = 0;
    internal const uint Incarnation = 1;
    internal const uint PolicyEpoch = 2;
    internal const uint Result = 3;
    internal const uint BlobAuthority = 4;
    internal const uint CompositionAuthority = 5;
    internal const uint ScopeKind = 6;
    internal const uint Partition = 7;
    internal const uint OnlineTextAuthority = 8;
}

internal static class SubscriptionCompletionFields
{
    internal const uint Generation = 0;
    internal const uint Position = 1;
    internal const uint Outcome = 2;
    internal const uint PolicyEpoch = 3;
}

internal static class TopicHeadFields
{
    internal const uint TailPosition = 0;
    internal const uint FirstAvailablePosition = 1;
    internal const uint Generation = 2;
    internal const uint StoredBytes = 3;
}

internal static class TopicHeadSnapshotFields
{
    internal const uint Head = 0;
    internal const uint StoredBytes = 1;
}

internal static class TopicPublicationProgressFields
{
    internal const uint Tail = 0;
    internal const uint Sequence = 1;
    internal const uint StoredBytes = 2;
}

internal static class ValidatedQueueLeaseFields
{
    internal const uint Claims = 0;
    internal const uint Metadata = 1;
    internal const uint BodyBytes = 2;
}
