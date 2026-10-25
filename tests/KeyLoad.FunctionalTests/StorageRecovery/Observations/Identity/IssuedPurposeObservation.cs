using KeyLoad.Clustering;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class IssuedPurposeObservation : IGrainRequestPurposeObserver
{
    private const string InvalidPurpose = "keyload-test-unsupported-request-purpose";
    private readonly Lock gate = new();
    private Guid commandId;
    private GrainRequestEnvelope? original;
    private bool received;

    internal void Arm(Guid value)
    {
        lock (gate)
        {
            if (value == Guid.Empty || commandId != Guid.Empty || original is not null)
            {
                throw new InvalidOperationException("The purpose observation is already owned.");
            }
            commandId = value;
        }
    }

    public string? BeforeIssue(GrainRequestEnvelope value)
    {
        lock (gate)
        {
            if (commandId == Guid.Empty || value.CommandId != commandId)
            {
                return null;
            }
            if (original is not null)
            {
                throw new InvalidOperationException("The purpose producer was repeated.");
            }
            original = value with
            {
                Payload = value.Payload.ToArray()
            };
            return InvalidPurpose;
        }
    }

    public void RejectedAfterDecode(GrainRequestEnvelope actual, KeyLoadException failure, GrainRequestScopeCut cut)
    {
        lock (gate)
        {
            if (original is not { } expected || received || failure.Code != ErrorCode.TokenInvalidated
                || actual.Purpose != InvalidPurpose || actual.RequestId != expected.RequestId
                || actual.Incarnation != expected.Incarnation || actual.PrincipalId != expected.PrincipalId
                || actual.ReadKind != expected.ReadKind || actual.CommandKind != expected.CommandKind
                || actual.CommandId != expected.CommandId || actual.ExpiresAt != expected.ExpiresAt
                || !actual.Payload.Span.SequenceEqual(expected.Payload.Span))
            {
                throw new InvalidOperationException("The genuine purpose receiver observation is invalid.");
            }
            GrainRequestScope.Validate(actual with
            {
                Purpose = expected.Purpose
            },
                cut.Incarnation, cut.EvaluatedAt, cut.MaximumFuture);
            received = true;
        }
    }

    internal void Disarm()
    {
        lock (gate)
        {
            if (!received || original is null)
            {
                throw new InvalidOperationException("The actual MAC-verified refusal is missing.");
            }
            commandId = Guid.Empty;
        }
    }
}
