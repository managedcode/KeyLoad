using System.Text;
using KeyLoad.CrashHost.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class C1OutcomeInspectionCodecTests
{
    [Test]
    public async Task AcCrs004V2RequestAndReceiptMatchLiteralGoldenBytes()
    {
        var input = C1OutcomeInspectionCodecInputs.Request();
        var requestBytes = C1OutcomeInspectionProtocol.SerializeRequest(input);
        await Assert.That(Encoding.UTF8.GetString(requestBytes)).IsEqualTo(C1OutcomeInspectionCodecInputs.RequestJson);
        await Assert.That(C1OutcomeInspectionJson.ReadRequest(requestBytes)).IsEqualTo(input);

        var receipt = C1OutcomeInspectionCodecInputs.Receipt();
        var receiptBytes = C1OutcomeInspectionJson.SerializeReceipt(receipt);
        await Assert.That(Encoding.UTF8.GetString(receiptBytes)).IsEqualTo(C1OutcomeInspectionCodecInputs.ReceiptJson + "\n");
        await Assert.That(C1OutcomeInspectionProtocol.DeserializeReceipt(receiptBytes)).IsEqualTo(receipt);
        var currentDirectory = Path.GetFullPath(Environment.CurrentDirectory);
        var currentRequest = C1OutcomeInspectionCodecInputs.Request(directory: currentDirectory);
        await Assert.That(C1OutcomeInspectionJson.ReadRequest(
            C1OutcomeInspectionProtocol.SerializeRequest(currentRequest))).IsEqualTo(currentRequest);
    }

    [Test]
    public async Task AcCrs004RequestRejectsMalformedClosedSchemaAndSafeErrors()
    {
        var valid = C1OutcomeInspectionCodecInputs.RequestJson;
        var invalid = new byte[][]
        {
            C1OutcomeInspectionCodecInputs.Encode(valid.Replace("{", "{\"Unknown\":1,", StringComparison.Ordinal)),
            C1OutcomeInspectionCodecInputs.Encode(valid.Replace("{", "{\"Version\":2,", StringComparison.Ordinal)),
            C1OutcomeInspectionCodecInputs.Encode(valid.Replace("\"CommandId\":", "\"Unused\":null,\"CommandId\":", StringComparison.Ordinal)),
            C1OutcomeInspectionCodecInputs.Encode(valid.Replace(",\"CommandId\":\"33333333-3333-4333-8333-333333333333\"", string.Empty, StringComparison.Ordinal)),
            C1OutcomeInspectionCodecInputs.Encode(valid.Replace("\"Directory\":\"/\"", "\"Directory\":null", StringComparison.Ordinal)),
            C1OutcomeInspectionCodecInputs.Encode(valid.Replace("\"PrincipalId\":\"principal-codec-canary\"", "\"PrincipalId\":null", StringComparison.Ordinal)),
            C1OutcomeInspectionCodecInputs.Encode(valid + "{}"),
            C1OutcomeInspectionCodecInputs.Encode(valid.Replace("\"Version\":2", "\"Version\":\"2\"", StringComparison.Ordinal)),
            C1OutcomeInspectionCodecInputs.Encode(valid.Replace("\"ExpectedNodeId\":\"11111111-1111-4111-8111-111111111111\"", "\"ExpectedNodeId\":17", StringComparison.Ordinal)),
            C1OutcomeInspectionCodecInputs.Encode(valid.Replace("\"Partition\":{", "\"Partition\":null", StringComparison.Ordinal)),
            C1OutcomeInspectionCodecInputs.Encode(valid.Replace("\"PartitionKey\":\"partition-codec\"", "\"Unexpected\":true,\"PartitionKey\":\"partition-codec\"", StringComparison.Ordinal)),
            C1OutcomeInspectionCodecInputs.Encode(valid.Replace("\"PartitionKey\":\"partition-codec\"", "\"PartitionKey\":\"partition-codec\",\"PartitionKey\":\"partition-codec\"", StringComparison.Ordinal)),
            C1OutcomeInspectionCodecInputs.Encode(valid.Replace("\"TenantId\":\"tenant-codec\"", "\"TenantId\":null", StringComparison.Ordinal)),
            C1OutcomeInspectionCodecInputs.Encode(valid.Replace(",\"PartitionKey\":\"partition-codec\"", string.Empty, StringComparison.Ordinal)),
            C1OutcomeInspectionCodecInputs.Encode(valid.Replace(",\"Partition\":{\"TenantId\":\"tenant-codec\",\"DatabaseId\":\"database-codec\",\"TransactionDomainId\":\"domain-codec\",\"PartitionKey\":\"partition-codec\"}", string.Empty, StringComparison.Ordinal)),
            [0xFF]
        };
        foreach (var bytes in invalid)
        {
            await AssertInvalidRequest(bytes);
        }
    }

    [Test]
    public async Task AcCrs004ReceiptRejectsMalformedClosedSchemaAndSafeErrors()
    {
        var valid = C1OutcomeInspectionCodecInputs.ReceiptJson;
        var invalid = new byte[][]
        {
            C1OutcomeInspectionCodecInputs.Encode(valid.Replace("{", "{\"Unknown\":1,", StringComparison.Ordinal)),
            C1OutcomeInspectionCodecInputs.Encode(valid.Replace("{", "{\"Version\":1,", StringComparison.Ordinal)),
            C1OutcomeInspectionCodecInputs.Encode(valid.Replace(",\"OutcomePresent\":true", string.Empty, StringComparison.Ordinal)),
            C1OutcomeInspectionCodecInputs.Encode(valid.Replace("\"NodeId\":\"11111111-1111-4111-8111-111111111111\"", "\"NodeId\":null", StringComparison.Ordinal)),
            C1OutcomeInspectionCodecInputs.Encode(valid + "{}"),
            C1OutcomeInspectionCodecInputs.Encode(valid.Replace("\"Position\":42", "\"Position\":\"42\"", StringComparison.Ordinal)),
            C1OutcomeInspectionCodecInputs.Encode(valid.Replace("\"OutcomePresent\":true", "\"OutcomePresent\":\"true\"", StringComparison.Ordinal)),
            [0xFF]
        };
        foreach (var bytes in invalid)
        {
            await AssertInvalidReceipt(bytes);
        }
    }

    [Test]
    public async Task AcCrs004RequestVersionIdsAndPrincipalUtf8BoundsAreExact()
    {
        await AssertInvalidRequest(C1OutcomeInspectionCodecInputs.Encode(
            C1OutcomeInspectionCodecInputs.RequestJson.Replace("\"Version\":2", "\"Version\":1", StringComparison.Ordinal)));
        await AssertInvalidRequest(C1OutcomeInspectionCodecInputs.Encode(C1OutcomeInspectionCodecInputs.RequestJson.Replace(
            "\"Directory\":\"/\"", "\"Directory\":\"/./\"", StringComparison.Ordinal)));
        await AssertInvalidRequest(C1OutcomeInspectionCodecInputs.Encode(C1OutcomeInspectionCodecInputs.RequestJson.Replace(
            C1OutcomeInspectionCodecInputs.NodeId.ToString(), Guid.Empty.ToString(), StringComparison.Ordinal)));
        await AssertInvalidRequest(C1OutcomeInspectionCodecInputs.Encode(C1OutcomeInspectionCodecInputs.RequestJson.Replace(
            C1OutcomeInspectionCodecInputs.Incarnation.ToString(), Guid.Empty.ToString(), StringComparison.Ordinal)));
        await AssertInvalidRequest(C1OutcomeInspectionCodecInputs.Encode(C1OutcomeInspectionCodecInputs.RequestJson.Replace(
            C1OutcomeInspectionCodecInputs.CommandId.ToString(), Guid.Empty.ToString(), StringComparison.Ordinal)));
        var asciiMaximum = C1OutcomeInspectionCodecInputs.Request(principal: new string('a', 256));
        await Assert.That(C1OutcomeInspectionJson.ReadRequest(C1OutcomeInspectionProtocol.SerializeRequest(asciiMaximum)).PrincipalId.Length)
            .IsEqualTo(256);
        await AssertInvalidRequest(C1OutcomeInspectionCodecInputs.Encode(
            C1OutcomeInspectionCodecInputs.RequestJson.Replace("principal-codec-canary", new string('a', 257), StringComparison.Ordinal)));
        var multibyteMaximum = C1OutcomeInspectionCodecInputs.Request(principal: new string('é', 128));
        await Assert.That(C1OutcomeInspectionJson.ReadRequest(C1OutcomeInspectionProtocol.SerializeRequest(multibyteMaximum)).PrincipalId)
            .IsEqualTo(new string('é', 128));
        await AssertInvalidRequest(C1OutcomeInspectionCodecInputs.RequestWithPrincipal(new string('é', 129)));
    }

    [Test]
    public async Task AcCrs004PartitionComponentsUseStrictUtf8Bounds()
    {
        var maximum = new string('a', C1OutcomeInspectionProtocol.MaximumPartitionComponentBytes);
        var maxPartition = new PartitionRef(maximum, maximum, maximum, maximum);
        var maxRequest = C1OutcomeInspectionCodecInputs.Request(partition: maxPartition);
        await Assert.That(C1OutcomeInspectionJson.ReadRequest(C1OutcomeInspectionProtocol.SerializeRequest(maxRequest)).Partition)
            .IsEqualTo(maxPartition);

        foreach (var component in new[] { nameof(PartitionRef.TenantId), nameof(PartitionRef.DatabaseId),
            nameof(PartitionRef.TransactionDomainId), nameof(PartitionRef.PartitionKey) })
        {
            var empty = C1OutcomeInspectionCodecInputs.RequestWithPartitionComponent(component, string.Empty);
            await AssertInvalidRequest(C1OutcomeInspectionCodecInputs.Encode(empty));
            var excessive = C1OutcomeInspectionCodecInputs.RequestWithPartitionComponent(component,
                new string('a', C1OutcomeInspectionProtocol.MaximumPartitionComponentBytes + 1));
            await AssertInvalidRequest(C1OutcomeInspectionCodecInputs.Encode(excessive));
        }

        var multibyte = C1OutcomeInspectionCodecInputs.RequestJson.Replace("tenant-codec",
            new string('é', C1OutcomeInspectionProtocol.MaximumPartitionComponentBytes / 2), StringComparison.Ordinal);
        await Assert.That(C1OutcomeInspectionJson.ReadRequest(C1OutcomeInspectionCodecInputs.Encode(multibyte)).Partition.TenantId)
            .IsEqualTo(new string('é', C1OutcomeInspectionProtocol.MaximumPartitionComponentBytes / 2));
        var excessiveUtf8 = C1OutcomeInspectionCodecInputs.RequestJson.Replace("tenant-codec",
            new string('é', C1OutcomeInspectionProtocol.MaximumPartitionComponentBytes / 2 + 1), StringComparison.Ordinal);
        await AssertInvalidRequest(C1OutcomeInspectionCodecInputs.Encode(excessiveUtf8));
    }

    [Test]
    public async Task AcCrs004ReceiptVersionIdsAndNumericRangesAreValidated()
    {
        await AssertInvalidReceipt(C1OutcomeInspectionJson.SerializeReceipt(C1OutcomeInspectionCodecInputs.Receipt(version: 1)));
        await AssertInvalidReceipt(C1OutcomeInspectionJson.SerializeReceipt(C1OutcomeInspectionCodecInputs.Receipt(nodeId: Guid.Empty)));
        await AssertInvalidReceipt(C1OutcomeInspectionJson.SerializeReceipt(C1OutcomeInspectionCodecInputs.Receipt(incarnation: Guid.Empty)));
        await AssertInvalidReceipt(C1OutcomeInspectionJson.SerializeReceipt(C1OutcomeInspectionCodecInputs.Receipt(formatVersion: 0)));
        await AssertInvalidReceipt(C1OutcomeInspectionJson.SerializeReceipt(C1OutcomeInspectionCodecInputs.Receipt(position: -1)));
        await Assert.That(C1OutcomeInspectionProtocol.DeserializeReceipt(
            C1OutcomeInspectionJson.SerializeReceipt(C1OutcomeInspectionCodecInputs.Receipt(outcomePresent: false))).OutcomePresent)
            .IsFalse();
    }

    [Test]
    public async Task AcCrs004InputAndReceiptByteBoundsAreInclusiveAndFixed()
    {
        var maxRequest = C1OutcomeInspectionCodecInputs.PadJson(C1OutcomeInspectionCodecInputs.RequestJson,
            C1OutcomeInspectionProtocol.MaximumRequestBytes);
        await Assert.That(C1OutcomeInspectionJson.ReadRequest(maxRequest).CommandId)
            .IsEqualTo(C1OutcomeInspectionCodecInputs.CommandId);
        await AssertInvalidRequest([.. maxRequest, (byte)' ']);

        var maxReceipt = C1OutcomeInspectionCodecInputs.PadJson(C1OutcomeInspectionCodecInputs.ReceiptJson + "\n",
            C1OutcomeInspectionProtocol.MaximumReceiptBytes);
        await Assert.That(C1OutcomeInspectionProtocol.DeserializeReceipt(maxReceipt).Position).IsEqualTo(42);
        await AssertInvalidReceipt([.. maxReceipt, (byte)' ']);
        await Assert.That(C1OutcomeInspectionProtocol.SerializeRequest(C1OutcomeInspectionCodecInputs.Request()).Length)
            .IsLessThanOrEqualTo(C1OutcomeInspectionProtocol.MaximumRequestBytes);
        await Assert.That(C1OutcomeInspectionJson.SerializeReceipt(C1OutcomeInspectionCodecInputs.Receipt()).Length)
            .IsLessThanOrEqualTo(C1OutcomeInspectionProtocol.MaximumReceiptBytes);
    }

    [Test]
    public async Task AcCrs004DtoDiagnosticsNeverRevealRequestOrOutcomeData()
    {
        var request = C1OutcomeInspectionCodecInputs.Request();
        var receipt = C1OutcomeInspectionCodecInputs.Receipt();
        await Assert.That(request.ToString()).IsEqualTo(nameof(C1OutcomeInspectionRequest));
        await Assert.That(receipt.ToString()).IsEqualTo(nameof(C1OutcomeInspectionReceipt));
        await Assert.That(request.ToString().Contains(C1OutcomeInspectionCodecInputs.Directory, StringComparison.Ordinal)).IsFalse();
        await Assert.That(request.ToString().Contains(C1OutcomeInspectionCodecInputs.Principal, StringComparison.Ordinal)).IsFalse();
        await Assert.That(request.ToString().Contains(C1OutcomeInspectionCodecInputs.CommandId.ToString(), StringComparison.Ordinal)).IsFalse();
        await Assert.That(receipt.ToString().Contains(C1OutcomeInspectionCodecInputs.NodeId.ToString(), StringComparison.Ordinal)).IsFalse();
        await Assert.That(C1OutcomeInspectionProtocol.InvalidRequest).IsEqualTo("The C1 outcome inspection request is invalid.");
        await Assert.That(C1OutcomeInspectionProtocol.InvalidReceipt).IsEqualTo("The C1 outcome inspection receipt is invalid.");
    }

    private static async Task AssertInvalidRequest(byte[] bytes)
    {
        var error = Assert.ThrowsExactly<InvalidDataException>(() => C1OutcomeInspectionJson.ReadRequest(bytes));
        await Assert.That(error.Message).IsEqualTo(C1OutcomeInspectionProtocol.InvalidRequest);
    }

    private static async Task AssertInvalidReceipt(byte[] bytes)
    {
        var error = Assert.ThrowsExactly<InvalidDataException>(() => C1OutcomeInspectionProtocol.DeserializeReceipt(bytes));
        await Assert.That(error.Message).IsEqualTo(C1OutcomeInspectionProtocol.InvalidReceipt);
    }
}
