using System.Text;
using KeyLoad.CrashHost.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class C1OutcomeInspectionCodecInputs
{
    internal static readonly Guid NodeId = new("11111111-1111-4111-8111-111111111111");
    internal static readonly Guid Incarnation = new("22222222-2222-4222-8222-222222222222");
    internal static readonly Guid CommandId = new("33333333-3333-4333-8333-333333333333");
    internal const string Principal = "principal-codec-canary";
    internal const string PartitionKey = "partition-codec";
    internal const string Directory = "/";
    internal const string RequestJson = "{\"Version\":2,\"Directory\":\"/\",\"ExpectedNodeId\":\"11111111-1111-4111-8111-111111111111\",\"Incarnation\":\"22222222-2222-4222-8222-222222222222\",\"PrincipalId\":\"principal-codec-canary\",\"CommandId\":\"33333333-3333-4333-8333-333333333333\",\"Partition\":{\"TenantId\":\"tenant-codec\",\"DatabaseId\":\"database-codec\",\"TransactionDomainId\":\"domain-codec\",\"PartitionKey\":\"" + PartitionKey + "\"}}";
    internal const string ReceiptJson = "{\"Version\":2,\"NodeId\":\"11111111-1111-4111-8111-111111111111\",\"Incarnation\":\"22222222-2222-4222-8222-222222222222\",\"FormatVersion\":1,\"Position\":42,\"OutcomePresent\":true}";

    internal static C1OutcomeInspectionRequest Request(string directory = Directory, string principal = Principal,
        Guid? nodeId = null, Guid? incarnation = null, Guid? commandId = null, int? version = null,
        PartitionRef? partition = null)
        => new(version ?? C1OutcomeInspectionProtocol.Version, directory, nodeId ?? NodeId,
            incarnation ?? Incarnation, principal, commandId ?? CommandId,
            partition ?? new("tenant-codec", "database-codec", "domain-codec", PartitionKey));

    internal static C1OutcomeInspectionReceipt Receipt(Guid? nodeId = null, Guid? incarnation = null,
        int version = C1OutcomeInspectionProtocol.Version, int formatVersion = 1, long position = 42,
        bool outcomePresent = true)
        => new(version, nodeId ?? NodeId, incarnation ?? Incarnation, formatVersion, position, outcomePresent);

    internal static byte[] Encode(string json) => Encoding.UTF8.GetBytes(json);

    internal static byte[] PadJson(string json, int exactBytes)
    {
        var original = Encode(json);
        ArgumentOutOfRangeException.ThrowIfLessThan(exactBytes, original.Length);
        var result = new byte[exactBytes];
        original.CopyTo(result, 0);
        result.AsSpan(original.Length).Fill((byte)' ');
        return result;
    }

    internal static string RequestWithPartitionComponent(string name, string value)
    {
        var current = name switch
        {
            nameof(PartitionRef.TenantId) => "tenant-codec",
            nameof(PartitionRef.DatabaseId) => "database-codec",
            nameof(PartitionRef.TransactionDomainId) => "domain-codec",
            nameof(PartitionRef.PartitionKey) => PartitionKey,
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
        var field = $"\"{name}\":\"{current}\"";
        return RequestJson.Replace(field, $"\"{name}\":{System.Text.Json.JsonSerializer.Serialize(value)}",
            StringComparison.Ordinal);
    }

    internal static byte[] RequestWithPrincipal(string principal)
        => Encode(RequestJson.Replace(Principal, principal, StringComparison.Ordinal));
}
