namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(FollowerDocumentInternalAliases.Capability)]
internal sealed record FollowerDocumentReadCapability(
    [property: Orleans.Id(0)] ReadFollowerDocumentRequestV1 Request,
    [property: Orleans.Id(1)] DatabaseCredentialWitness Credential);
