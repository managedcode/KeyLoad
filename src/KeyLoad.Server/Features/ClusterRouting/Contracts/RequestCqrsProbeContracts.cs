using System.Text.Json.Serialization;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal enum RequestCqrsProbePhase
{
    RequestStarted,
    AuthorizationReload,
    BeforeSubmit,
    SubmitReturned,
    ProducerDisposed
}

internal enum RequestCqrsProbeAction
{
    Hold,
    ThrowOrdinary
}

internal enum RequestCqrsProbeOutcome
{
    Observed,
    Released,
    Cancelled,
    FaultRequested
}

internal readonly record struct RequestCqrsProbeOwnerRecord(
    [property: JsonRequired] int Version,
    [property: JsonRequired] string Kind,
    [property: JsonRequired] string SessionId,
    [property: JsonRequired] string Voter);

internal readonly record struct RequestCqrsProbeArmRecord(
    [property: JsonRequired] int Version,
    [property: JsonRequired] string Kind,
    [property: JsonRequired] string SessionId,
    [property: JsonRequired] Guid ArmId,
    [property: JsonRequired] string PrincipalId,
    [property: JsonRequired] Guid CommandId,
    [property: JsonRequired] GrainReadKind? ReadKind,
    [property: JsonRequired] RequestCqrsProbePhase Phase,
    [property: JsonRequired] RequestCqrsProbeAction Action);

internal readonly record struct RequestCqrsProbeReleaseRecord(
    [property: JsonRequired] int Version,
    [property: JsonRequired] string Kind,
    [property: JsonRequired] string SessionId,
    [property: JsonRequired] Guid ArmId,
    [property: JsonRequired] Guid RequestId);

internal readonly record struct RequestCqrsProbeMarkerRecord(
    [property: JsonRequired] int Version,
    [property: JsonRequired] string Kind,
    [property: JsonRequired] string SessionId,
    [property: JsonRequired] Guid ArmId,
    [property: JsonRequired] Guid RequestId,
    [property: JsonRequired] Guid CommandId,
    [property: JsonRequired] RequestCqrsProbePhase Phase,
    [property: JsonRequired] RequestCqrsProbeOutcome Outcome,
    [property: JsonRequired] string Voter,
    [property: JsonRequired] string SiloAddress);
