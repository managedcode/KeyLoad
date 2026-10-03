namespace KeyLoad.Core;

/// <summary>Requests a conditional update to one cluster membership row.</summary>
/// <param name="Key">Stable membership row key.</param>
/// <param name="ExpectedVersion">Required current row version.</param>
/// <param name="Json">Canonical membership payload.</param>

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.MembershipMutation)]
public sealed record MembershipMutation(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.MembershipMutationFields.Key)] string Key,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.MembershipMutationFields.ExpectedVersion)] long ExpectedVersion,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.MembershipMutationFields.Payload)] string Json);
/// <summary>Stores the versioned payload of one cluster membership row.</summary>
/// <param name="Version">Committed row version.</param>
/// <param name="Json">Canonical membership payload.</param>

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.MembershipRecord)]
public sealed record MembershipRecord(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.MembershipRecordFields.Version)] long Version,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.MembershipRecordFields.Payload)] string Json);
