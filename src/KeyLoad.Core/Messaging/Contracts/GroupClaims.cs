namespace KeyLoad.Core;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.InternalSerialization.CoreAliases.GroupClaims)]
internal sealed record GroupClaims(
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.GroupClaimsFields.Purpose)] string Purpose,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.GroupClaimsFields.Incarnation)] Guid Incarnation,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.GroupClaimsFields.Subscription)] SubscriptionRef Subscription,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.GroupClaimsFields.Generation)] long Generation,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.GroupClaimsFields.OwnershipEpoch)] long OwnershipEpoch,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.GroupClaimsFields.Position)] long Position,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.GroupClaimsFields.LeaseVersion)] long LeaseVersion,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.GroupClaimsFields.PrincipalId)] string PrincipalId,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.GroupClaimsFields.PolicyEpoch)] long PolicyEpoch,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.GroupClaimsFields.DataPolicyEpoch)] long DataPolicyEpoch);
