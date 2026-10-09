using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class RequestCqrsProbeMigrationJson(RequestCqrsProbeJson json, IOptions<RequestProbeExecutionOptions> options)
{
    private static readonly string[] Fields = [nameof(RequestCqrsProbeMigrationRecord.Version), nameof(RequestCqrsProbeMigrationRecord.Kind),
        nameof(RequestCqrsProbeMigrationRecord.SessionId), nameof(RequestCqrsProbeMigrationRecord.ArmId),
        nameof(RequestCqrsProbeMigrationRecord.RequestId), nameof(RequestCqrsProbeMigrationRecord.CommandId),
        nameof(RequestCqrsProbeMigrationRecord.Voter), nameof(RequestCqrsProbeMigrationRecord.SiloAddress),
        nameof(RequestCqrsProbeMigrationRecord.GrainDigest), nameof(RequestCqrsProbeMigrationRecord.ActivationId),
        nameof(RequestCqrsProbeMigrationRecord.TargetVoter), nameof(RequestCqrsProbeMigrationRecord.TargetSiloAddress)];

    internal RequestCqrsProbeMigrationRecord Read(ReadOnlySpan<byte> bytes)
    {
        var value = json.Read(bytes, Fields, RequestCqrsProbeMigrationJsonContext.Default.RequestCqrsProbeMigrationRecord);
        RequestCqrsProbeMigrationValidation.Require(value);
        return value;
    }
    internal byte[] Write(RequestCqrsProbeMigrationRecord value)
    {
        RequestCqrsProbeMigrationValidation.Require(value);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, RequestCqrsProbeMigrationJsonContext.Default.RequestCqrsProbeMigrationRecord);
        if (bytes.Length > options.Value.MaximumRecordBytes)
        { throw new InvalidOperationException(RequestCqrsProbeProtocol.InvalidRecord); }
        return bytes;
    }
}
