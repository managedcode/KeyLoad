using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsQueryReadCutValidationTests
{
    private const string PartitionField = "Partition";
    private const string TenantIdField = "TenantId";
    private const string DatabaseIdField = "DatabaseId";
    private const string TransactionDomainIdField = "TransactionDomainId";
    private const string PartitionKeyField = "PartitionKey";
    private const string UnknownField = "Unknown";
    private const string ResultCutPositionField = "ResultCutPosition";
    private const string ActivationIdField = "ActivationId";

    [Test]
    public async Task DeclaredParserInputRoundTripsEveryFieldAtTheActualRecordBoundary()
    {
        var input = new RequestCqrsQueryReadCutValidationInput();
        await Assert.That(input.Read(input.Write())).IsEqualTo(input.Value);
        await Assert.That(input.Read(input.PadTo(input.Settings.MaximumRecordBytes))).IsEqualTo(input.Value);
    }

    [Test]
    public async Task MalformedPartitionFieldsRefuseAndLeaveOriginalInputHealthy()
    {
        var input = new RequestCqrsQueryReadCutValidationInput();
        Action<System.Text.Json.Nodes.JsonObject>[] mutations =
        [
            value => value[PartitionField] = null,
            value => value[PartitionField]![TenantIdField] = null,
            value => value[PartitionField]![DatabaseIdField] = "",
            value => value[PartitionField]![TransactionDomainIdField] = " ",
            value => value[PartitionField]!.AsObject().Remove(PartitionKeyField),
            value => value[PartitionField]![UnknownField] = "value"
        ];
        foreach (var change in mutations)
        {
            _ = Assert.ThrowsExactly<InvalidOperationException>(() => input.Read(input.Change(change)));
            await Assert.That(input.Read(input.Write())).IsEqualTo(input.Value);
        }
    }

    [Test]
    public async Task MissingUnknownDuplicateAndEscapedFieldsRefuse()
    {
        var input = new RequestCqrsQueryReadCutValidationInput();
        byte[][] invalid =
        [
            input.Change(value => value.Remove(ResultCutPositionField)),
            input.Change(value => value[UnknownField] = true),
            input.DuplicateOrEscapeVersion(duplicate: true),
            input.DuplicateOrEscapeVersion(duplicate: false)
        ];
        foreach (var bytes in invalid)
        {
            _ = Assert.ThrowsExactly<InvalidOperationException>(() => input.Read(bytes));
            await Assert.That(input.Read(input.Write())).IsEqualTo(input.Value);
        }
    }

    [Test]
    public async Task OriginalUtf8AndByteBoundsRefuseWithoutChangingParserInput()
    {
        var input = new RequestCqrsQueryReadCutValidationInput();
        var invalidUtf8 = input.Write();
        invalidUtf8[0] = 0xff;
        _ = Assert.ThrowsExactly<InvalidOperationException>(() => input.Read(invalidUtf8));
        _ = Assert.ThrowsExactly<InvalidOperationException>(() =>
            input.Read(input.PadTo(checked(input.Settings.MaximumRecordBytes + 1))));
        await Assert.That(input.Read(input.Write())).IsEqualTo(input.Value);
    }

    [Test]
    public async Task MalformedNativeActivationCannotQualifyAQueryWitness()
    {
        var input = new RequestCqrsQueryReadCutValidationInput();
        foreach (var identity in new[] { "", " ", "not-an-activation" })
        {
            _ = Assert.ThrowsExactly<InvalidOperationException>(() =>
                input.Read(input.Change(value => value[ActivationIdField] = identity)));
            await Assert.That(input.Read(input.Write())).IsEqualTo(input.Value);
        }
        RequestCqrsProbeActivationValidation.RequireActivationId(input.Value.ActivationId);
    }
}
