using System.Text;
using System.Text.Json;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private string MovementPhaseIdentityJson(PartitionMovePhaseCommand actual)
    {
        if (NativeSerialization.Measure(actual) > Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        var identity = actual with { ReceiverEffectAdmission = null };
        if (actual.Stage == PartitionMovePeerStage.RetireCancel)
        {
            identity = actual with
            {
                Body = CanonicalMoveRetireCancellationBody(actual.Body),
                ReceiverEffectAdmission = null
            };
        }
        else if (actual.Stage == PartitionMovePeerStage.ControlAdmitCommand)
        {
            var body = NativeSerialization.Deserialize<PartitionControlAdmitBody>(actual.Body.Span);
            var canonical = body with
            {
                OriginalOperation = body.OriginalOperation with { EvaluatedAt = default },
                ExpiresAt = default
            };
            if (NativeSerialization.Measure(canonical) > Limits.MaxBatchBytes)
            { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
            identity = actual with { Body = NativeSerialization.Serialize(canonical), ReceiverEffectAdmission = null };
        }
        var json = JsonSerializer.Serialize(identity, JsonDefaults.Options);
        if (Encoding.UTF8.GetByteCount(json) > Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        return json;
    }
}
