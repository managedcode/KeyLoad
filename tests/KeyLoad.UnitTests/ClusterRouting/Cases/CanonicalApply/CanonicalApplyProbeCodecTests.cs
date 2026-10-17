using System.Text;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class CanonicalApplyProbeCodecTests
{
    private const string PartitionKey = "partition";
    private const string SourceArm = "\"00000000-0000-0000-0000-000000000005\"";
    private const string Scope = "{\"TenantId\":\"tenant\",\"DatabaseId\":\"database\",\"TransactionDomainId\":\"domain\",\"PartitionKey\":\"partition\"}";
    [Test]
    public async Task ExactCurrentCanonicalScopeRejectsOldMissingDuplicateAndUnarmableShapesThenReadsHealthy()
    {
        var ordinary = Encoding.UTF8.GetString(RequestCqrsProbeCodecInput.Arm());
        var valid = ordinary.Replace("\"Phase\":\"RequestStarted\"", "\"Phase\":\"CanonicalJournalFlushed\"", StringComparison.Ordinal)
            .Replace("\"Partition\":null", "\"Partition\":" + Scope, StringComparison.Ordinal)
            .Replace("\"SourceRequestId\":null", "\"SourceRequestId\":" + RequestCqrsProbeCodecInput.RequestIdValue, StringComparison.Ordinal)
            .Replace("\"TargetVoter\":null", "\"TargetVoter\":\"http://node-two/\"", StringComparison.Ordinal)
            .Replace("\"SourceArmId\":null", "\"SourceArmId\":" + SourceArm, StringComparison.Ordinal);
        var invalid = new[]
        {
            valid.Replace("\"Version\":2", "\"Version\":1", StringComparison.Ordinal),
            valid.Replace("\"Partition\":" + Scope, "\"Partition\":null", StringComparison.Ordinal),
            valid.Replace("\"TenantId\":\"tenant\"", "\"TenantId\":\"tenant\",\"TenantId\":\"tenant\"", StringComparison.Ordinal),
            valid.Replace("\"TenantId\":\"tenant\"", "\"TenantId\":[]", StringComparison.Ordinal),
            valid.Replace("CanonicalJournalFlushed", "CanonicalOutboundObserved", StringComparison.Ordinal),
            valid.Replace("CanonicalJournalFlushed", "CanonicalIndependentAppendCompleted", StringComparison.Ordinal),
            valid.Replace("CanonicalJournalFlushed", "CanonicalOwnerDisposed", StringComparison.Ordinal),
            valid.Replace("\"SourceRequestId\":" + RequestCqrsProbeCodecInput.RequestIdValue, "\"SourceRequestId\":null", StringComparison.Ordinal),
            valid.Replace("\"SourceArmId\":" + SourceArm, "\"SourceArmId\":null", StringComparison.Ordinal),
            valid.Replace("\"TargetVoter\":\"http://node-two/\"", "\"TargetVoter\":null", StringComparison.Ordinal),
            valid.Replace("CanonicalJournalFlushed", "RequestStarted", StringComparison.Ordinal),
            valid.Replace("\"Action\":\"Hold\"", "\"Action\":\"ThrowOrdinary\"", StringComparison.Ordinal)
        };
        foreach (var value in invalid)
        {
            await RequestCqrsProbeCodecAssertions.InvalidArmAsync(Encoding.UTF8.GetBytes(value));
        }
        var arm = UnitRequestProbeOptions.Json.ReadArm(Encoding.UTF8.GetBytes(valid));
        await Assert.That(arm.Partition?.ToPartition()).IsEqualTo(new PartitionRef("tenant", "database", "domain", PartitionKey));
        await Assert.That(arm.CommandId).IsEqualTo(RequestCqrsProbeCodecInput.CommandId);
        await Assert.That(arm.Phase).IsEqualTo(RequestCqrsProbePhase.CanonicalJournalFlushed);
        await Assert.That(UnitRequestProbeOptions.Json.ReadArm(RequestCqrsProbeCodecInput.Arm()).Partition).IsNull();
    }
}
