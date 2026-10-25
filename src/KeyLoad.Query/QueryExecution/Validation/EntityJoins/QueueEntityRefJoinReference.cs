using System.Text.Json;
using KeyLoad.Core;

namespace KeyLoad.Query.QueryExecution;

/// <summary>Validates the complete untrusted payload reference before selecting a execution document key.</summary>
internal static class QueueEntityRefJoinReference
{
    private const string PayloadField = "payload";
    private const string ReferenceField = "to";
    private const string InvalidReference = "The queue join reference does not match its declared partition and collection.";

    internal static global::ManagedCode.Communication.Result<EntityRef?> Read(JsonElement row, PartitionRef partition, string collection)
    {
        if (!row.TryGetProperty(PayloadField, out var payload) || payload.ValueKind == JsonValueKind.Null
            || !payload.TryGetProperty(ReferenceField, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return global::ManagedCode.Communication.Result<EntityRef?>.FailNotFound(nameof(Read));
        }
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidReference);
        }
        EntityRef reference;
        try
        {
            reference = JsonSerializer.Deserialize<EntityRef>(value.GetRawText(), JsonDefaults.Options)
                ?? throw Errors.Fail(ErrorCode.Validation, InvalidReference);
        }
        catch (JsonException)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidReference);
        }
        if (reference.Partition is null || reference.Partition != partition || reference.Collection != collection)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidReference);
        }
        EngineResourceAccess.ValidatePartition(reference.Partition);
        JsonData.Identifier(reference.Collection);
        JsonData.Identifier(reference.Id);
        return global::ManagedCode.Communication.Result<EntityRef?>.Succeed(((EntityRef?)(reference)));
    }
}
