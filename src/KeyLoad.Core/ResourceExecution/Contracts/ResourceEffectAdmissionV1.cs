using System.Collections.Immutable;

namespace KeyLoad.Core.Features.ResourceExecution;

[Orleans.GenerateSerializer]
[Orleans.Alias(ResourceEffectAdmissionProtocol.Alias)]
internal sealed record ResourceEffectAdmissionV1(
    [property: Orleans.Id(ResourceEffectAdmissionProtocol.VersionId)] int Version,
    [property: Orleans.Id(ResourceEffectAdmissionProtocol.FingerprintId)] string OperationFingerprint,
    [property: Orleans.Id(ResourceEffectAdmissionProtocol.BindingsId)] ImmutableArray<ResourceLifetimeV1> Bindings,
    [property: Orleans.Id(ResourceEffectAdmissionProtocol.ErrorId)] ErrorCode? Error,
    [property: Orleans.Id(ResourceEffectAdmissionProtocol.DetailId)] string? SafeDetail);
