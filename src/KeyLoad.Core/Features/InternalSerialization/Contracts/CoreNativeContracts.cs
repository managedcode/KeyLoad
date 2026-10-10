using System.Text.Json.Serialization;
using KeyLoad.Core.Features.BlobStorage;

namespace KeyLoad.Core;

internal enum GroupDeliveryState { Pending, Leased, Acked, Filtered, Parked }

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.EventIdentity)]
internal sealed record EventIdentity(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.EventIdentityFields.StreamId)] string StreamId,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.EventIdentityFields.Generation)] long Generation,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.EventIdentityFields.Revision)] long Revision);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.TopicHead)]
internal sealed record TopicHead(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.TopicHeadFields.TailPosition)] long TailPosition,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.TopicHeadFields.FirstAvailablePosition)] long FirstAvailablePosition,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.TopicHeadFields.Generation)] long Generation,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.TopicHeadFields.StoredBytes)] long StoredBytes);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.SourceCursor)]
internal sealed record SourceCursor(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.SourceCursorFields.Purpose)] string Purpose,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.SourceCursorFields.Incarnation)] Guid Incarnation,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.SourceCursorFields.Source)] EventSourceRef Source,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.SourceCursorFields.PrincipalId)] string PrincipalId,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.SourceCursorFields.PolicyEpoch)] long PolicyEpoch,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.SourceCursorFields.SchemaVersion)] long SchemaVersion,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.SourceCursorFields.Position)] long Position,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.SourceCursorFields.ExpiresAt)] DateTimeOffset ExpiresAt);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.ProjectionBatchClaims)]
internal sealed record ProjectionBatchClaims(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.ProjectionBatchClaimsFields.Purpose)] string Purpose,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.ProjectionBatchClaimsFields.Incarnation)] Guid Incarnation,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.ProjectionBatchClaimsFields.Consumer)] ProjectionConsumerRef Consumer,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.ProjectionBatchClaimsFields.IndexGeneration)] long IndexGeneration,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.ProjectionBatchClaimsFields.After)] long After,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.ProjectionBatchClaimsFields.Through)] long Through,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.ProjectionBatchClaimsFields.ExpiresAt)] DateTimeOffset ExpiresAt);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.ProjectionReceipt)]
internal sealed record ProjectionReceipt(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.ProjectionReceiptFields.Fingerprint)] string Fingerprint,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.ProjectionReceiptFields.Receipt)] CommitReceipt Receipt,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.ProjectionReceiptFields.Checkpoint)] long Checkpoint);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.GroupState)]
internal sealed record GroupState(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupStateFields.Definition)] SubscriptionDefinition Definition,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupStateFields.Generation)] long Generation,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupStateFields.OwnershipEpoch)] long OwnershipEpoch,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupStateFields.Checkpoint)] long Checkpoint,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupStateFields.IssuedPosition)] long IssuedPosition,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupStateFields.Paused)] bool Paused = false,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupStateFields.SafeFailureCode)] string? SafeFailureCode = null);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.GroupDelivery)]
internal sealed record GroupDelivery(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupDeliveryFields.Position)] long Position,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupDeliveryFields.State)] GroupDeliveryState State,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupDeliveryFields.Attempts)] int Attempts,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupDeliveryFields.LeaseVersion)] long LeaseVersion,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupDeliveryFields.AvailableAt)] DateTimeOffset? AvailableAt = null,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupDeliveryFields.LeaseUntil)] DateTimeOffset? LeaseUntil = null,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupDeliveryFields.PrincipalId)] string? PrincipalId = null);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.GroupClaims)]
internal sealed record GroupClaims(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupClaimsFields.Purpose)] string Purpose,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupClaimsFields.Incarnation)] Guid Incarnation,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupClaimsFields.Subscription)] SubscriptionRef Subscription,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupClaimsFields.Generation)] long Generation,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupClaimsFields.OwnershipEpoch)] long OwnershipEpoch,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupClaimsFields.Position)] long Position,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupClaimsFields.LeaseVersion)] long LeaseVersion,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupClaimsFields.PrincipalId)] string PrincipalId,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupClaimsFields.PolicyEpoch)] long PolicyEpoch,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupClaimsFields.DataPolicyEpoch)] long DataPolicyEpoch);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.SubscriptionCompletion)]
internal sealed record SubscriptionCompletion(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.SubscriptionCompletionFields.Generation)] long Generation,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.SubscriptionCompletionFields.Position)] long Position,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.SubscriptionCompletionFields.Outcome)] string Outcome,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.SubscriptionCompletionFields.PolicyEpoch)] long PolicyEpoch);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.InboxRecord)]
internal sealed record InboxRecord(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.InboxRecordFields.Fingerprint)] string Fingerprint,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.InboxRecordFields.Receipt)] CommitReceipt Receipt);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.StoredOutcome)]
internal sealed record StoredOutcome(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredOutcomeFields.Fingerprint)] string Fingerprint,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredOutcomeFields.Incarnation)] Guid Incarnation,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredOutcomeFields.PolicyEpoch)] long PolicyEpoch,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredOutcomeFields.Result)] OperationResult Result)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredOutcomeFields.BlobAuthority)]
    public BlobOutcomeAuthority? BlobAuthority { get; init; }

    [JsonIgnore]
    [global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredOutcomeFields.CompositionAuthority)]
    public global::KeyLoad.Core.Features.DatabaseComposition.CompositionOutcomeAuthority? CompositionAuthority { get; init; }

    [JsonIgnore]
    [global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredOutcomeFields.ScopeKind)]
    public global::KeyLoad.Core.Features.ClusterRouting.Contracts.CommandOutcomeScopeKind ScopeKind { get; init; }

    [JsonIgnore]
    [global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredOutcomeFields.Partition)]
    public PartitionRef? Partition { get; init; }

    [JsonIgnore]
    [global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredOutcomeFields.OnlineTextAuthority)]
    public global::KeyLoad.Core.Features.Search.OnlineTextOutcomeAuthority? OnlineTextAuthority { get; init; }
}

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.ReadyClaimInput)]
internal sealed record ReadyClaimInput(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.ReadyClaimInputFields.ReadyKey)] byte[] ReadyKey,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.ReadyClaimInputFields.MetadataKey)] byte[] MetadataKey,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.ReadyClaimInputFields.Metadata)] MessageMetadata Metadata,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.ReadyClaimInputFields.TransitionKey)] byte[] TransitionKey,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.ReadyClaimInputFields.DeletesBody)] bool DeletesBody,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.ReadyClaimInputFields.StrictOrderKey)] byte[]? StrictOrderKey = null);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.TopicHeadSnapshot)]
internal readonly record struct TopicHeadSnapshot(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.TopicHeadSnapshotFields.Head)] EventSourceHead Head,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.TopicHeadSnapshotFields.StoredBytes)] long StoredBytes);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.TopicPublicationProgress)]
internal readonly record struct TopicPublicationProgress(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.TopicPublicationProgressFields.Tail)] long Tail,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.TopicPublicationProgressFields.Sequence)] long Sequence,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.TopicPublicationProgressFields.StoredBytes)] long StoredBytes);
