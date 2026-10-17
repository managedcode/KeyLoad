namespace KeyLoad.Core.Features.BlobStorage;

/// <summary>Binds a successful cached blob outcome to one immutable upload lifetime.</summary>
/// <param name="FormatVersion">The private authority stamp format.</param>
/// <param name="BeginCommandId">The command that created this upload lifetime.</param>
/// <param name="CreatorPrincipalId">The persisted principal that created this lifetime.</param>

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.BlobOutcomeAuthority)]
public sealed record BlobOutcomeAuthority(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobOutcomeAuthorityFields.FormatVersion)] int FormatVersion,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobOutcomeAuthorityFields.BeginCommandId)] Guid BeginCommandId,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobOutcomeAuthorityFields.CreatorPrincipalId)] string CreatorPrincipalId);
