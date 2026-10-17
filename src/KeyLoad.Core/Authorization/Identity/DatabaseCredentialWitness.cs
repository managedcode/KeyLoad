namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(AuthorizationNativeAliases.CredentialWitness)]
internal sealed record DatabaseCredentialWitness(
    [property: Orleans.Id(0)] string CredentialId,
    [property: Orleans.Id(1)] ReadOnlyMemory<byte> Digest);
