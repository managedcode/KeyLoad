namespace KeyLoad.Core;

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
