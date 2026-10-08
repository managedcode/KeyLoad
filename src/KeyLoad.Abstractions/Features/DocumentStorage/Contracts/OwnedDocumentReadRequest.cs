namespace KeyLoad;

/// <summary>Receiving-only trusted scope for a fresh independently authorized document read.</summary>
/// <param name="Tenant">Verified source subject tenant, never grants.</param>
/// <param name="Request">Original public document request.</param>
/// <param name="Owner">Configured exact receiving physical owner.</param>
[Orleans.GenerateSerializer, Orleans.Alias(RemoteDocumentAliases.Request)]
public sealed record OwnedDocumentReadRequestV1(
    [property: Orleans.Id(0)] string Tenant,
    [property: Orleans.Id(1)] GetDocumentRequest Request,
    [property: Orleans.Id(2)] PhysicalShardRecord Owner);
