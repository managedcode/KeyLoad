using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyLoad.Replication;

internal static class ReplicaReadRoundGuard
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    internal static AppendRequest Probe(ReadOnlySpan<byte> bytes, ReplicaConfiguration configuration)
    {
        if (bytes.Length > (long)configuration.MaxAppendBytes + ReplicaProtocol.PayloadMetadataBytes)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidAppend);
        }
        var request = Read<AppendRequest>(bytes);
        if (request.Entries.IsDefault || request.Entries.Length != 0 || request.Term < 1
            || request.PreviousIndex < 0 || request.PreviousIndex == long.MaxValue
            || request.PreviousTerm < 0 || request.PreviousTerm > request.Term || request.CommittedIndex < 0
            || (request.PreviousIndex == 0) != (request.PreviousTerm == 0)
            || !configuration.VoterIds.Contains(request.LeaderId, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidAppend);
        }
        return request;
    }

    internal static void Empty(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > ReplicaProtocol.PayloadMetadataBytes || Read<string>(bytes).Length != 0)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidPeer);
        }
    }

    private static T Read<T>(ReadOnlySpan<byte> bytes) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(bytes, Options)
                ?? throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidAppend);
        }
        catch (JsonException)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidAppend);
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonDefaults.Options)
        {
            AllowDuplicateProperties = false,
            PropertyNameCaseInsensitive = false,
            RespectRequiredConstructorParameters = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Insert(0, new ReplicaOperationJsonConverter());
        return options;
    }
}
