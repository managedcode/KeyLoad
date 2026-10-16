using System.Collections.Immutable;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private byte[] CaptureResourceEffectAdmission(ReplicatedOperation operation, NativeCommandPayload payload)
    {
        var fingerprint = NativeOperationFingerprint.Compute(operation, payload.Value);
        if (payload.Error is not null)
        {
            return EncodeResourceEffectAdmission(new(ResourceEffectAdmissionProtocol.Version,
                fingerprint, [], payload.Error, payload.SafeDetail));
        }
        var candidate = operation with
        {
            NativePayload = NativeSerialization.Serialize(payload)
        };
        var captured = Store.Read(raw => CaptureResourceEffectAdmission(raw, candidate, fingerprint));
        return EncodeResourceEffectAdmission(captured);
    }

    private byte[] CaptureResourceEffectAdmission(ReplicatedOperation operation, NativeCommandPayload payload,
        ReadExecutionBudget work)
    {
        work.Check();
        var fingerprint = NativeOperationFingerprint.Compute(operation, payload.Value);
        if (payload.Error is not null)
        {
            return EncodeResourceEffectAdmission(new(ResourceEffectAdmissionProtocol.Version,
                fingerprint, [], payload.Error, payload.SafeDetail));
        }

        var candidate = operation with
        {
            NativePayload = NativeSerialization.Serialize(payload)
        };
        var captured = Store.Read(raw => CaptureResourceEffectAdmission(work.CreateView(raw), candidate,
            fingerprint, preserveBudgetFailure: true));
        work.Check();
        return EncodeResourceEffectAdmission(captured);
    }

    private ResourceEffectAdmissionV1 CaptureResourceEffectAdmission(IKeyValueView raw,
        ReplicatedOperation operation, string fingerprint, bool preserveBudgetFailure = false)
    {
        var scope = new ResourceEffectBindingScope(OperationLimitsOptions);
        var view = new ResourceEffectObservationView(raw, scope);
        try
        {
            var principal = Principal(view, operation.PrincipalId, operation.EvaluatedAt);
            AuthorizeOperation(view, principal, operation);
            ObserveAdditionalResourceEffects(view, principal, operation);
            var admission = new ResourceEffectAdmissionV1(ResourceEffectAdmissionProtocol.Version,
                fingerprint, scope.Capture(), null, null);
            scope.Activate(view, admission);
            return admission;
        }
        catch (KeyLoadException error) when (!preserveBudgetFailure || error.Code != ErrorCode.BudgetExceeded)
        {
            return new(ResourceEffectAdmissionProtocol.Version, fingerprint,
                ImmutableArray<ResourceLifetimeV1>.Empty,
                error.Code, error.Message);
        }
    }

    private byte[] EncodeResourceEffectAdmission(ResourceEffectAdmissionV1 admission)
    {
        var bytes = NativeSerialization.Serialize(admission);
        RequireNativeBudget(bytes.Length);
        return bytes;
    }
}
