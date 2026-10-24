using System.Collections.Immutable;

namespace KeyLoad.Core.ResourceExecution;

[Orleans.GenerateSerializer]
[Orleans.Alias(ResourceEffectAdmissionProtocol.Alias)]
internal sealed record ResourceEffectAdmission(
    [property: Orleans.Id(ResourceEffectAdmissionProtocol.VersionId)] int Version,
    [property: Orleans.Id(ResourceEffectAdmissionProtocol.FingerprintId)] string OperationFingerprint,
    [property: Orleans.Id(ResourceEffectAdmissionProtocol.BindingsId)] ImmutableArray<ResourceLifetime> Bindings,
    [property: Orleans.Id(ResourceEffectAdmissionProtocol.ErrorId)] ErrorCode? Error,
    [property: Orleans.Id(ResourceEffectAdmissionProtocol.DetailId)] string? SafeDetail);
