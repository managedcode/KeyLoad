using System.Text.Json;
using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class CanonicalJsonTests
{
    private const string NestedInput = "{\"z\":null,\"a\":{\"é\":\"line\\n\",\"B\":1e+01}}";
    private const string NestedCanonical = "{\"a\":{\"B\":10,\"\\u00E9\":\"line\\n\"},\"z\":null}";
    private const string OrderedFingerprint = "d898aa36fe59fe1b7fcbcad5943548b75b80e65bf34a76b209c9eae8086dd0d0";
    private const string RawExponentFingerprint = "fb820168b64ed270502c8d71a9c4ad4d5e28a4cd00541c18262acbc2b09e9f10";
    private const string DecimalTenFingerprint = "4a44dc15364204a80fe80e9039455cc1608281820fe2b24f1e5233ade6af1dd5";
    private const string NullFingerprint = "74234e98afe7498fb5daf1f36ac2d78acc339464f950703b8c019892f982b90b";
    private const string NestedUnicodeInput = "{\"z\":[null,{\"β\":\"é\"}],\"a\":1}";
    private const string NestedUnicodeCanonical = "{\"a\":1,\"z\":[null,{\"\\u03B2\":\"\\u00E9\"}]}";
    private const string NestedUnicodeFingerprint = "cafc88a973c699e5db69399e00f61b38d489575f2a8000f72715ea03be27f981";
    private const string ZPropertyName = "z";
    private const string APropertyName = "a";

    [Test]
    public async Task AcMp006CanonicalValidationOrdersNestedPropertiesAndPreservesEscaping()
    {
        var canonical = JsonData.Validate(NestedInput, new DatabaseLimits());

        await Assert.That(canonical).IsEqualTo(NestedCanonical);
        await Assert.That(JsonData.Validate("1.2300", new DatabaseLimits(), requireObject: false)).IsEqualTo("1.23");
        await Assert.That(JsonData.Validate("null", new DatabaseLimits(), requireObject: false)).IsEqualTo("null");
    }

    [Test]
    public async Task AcMp006FingerprintGoldenKeepsRawNumbersOrdinalKeysAndNull()
    {
        var unordered = new Dictionary<string, object?> { [ZPropertyName] = null, [APropertyName] = 1 };
        using var rawExponent = JsonDocument.Parse("1e+01");
        using var decimalTen = JsonDocument.Parse("10");

        await Assert.That(JsonData.Fingerprint(unordered)).IsEqualTo(OrderedFingerprint);
        await Assert.That(JsonData.Fingerprint(rawExponent.RootElement)).IsEqualTo(RawExponentFingerprint);
        await Assert.That(JsonData.Fingerprint(decimalTen.RootElement)).IsEqualTo(DecimalTenFingerprint);
        await Assert.That(JsonData.Fingerprint<object?>(null)).IsEqualTo(NullFingerprint);
    }

    [Test]
    public async Task AcMp006NestedArraysAndUnicodeKeepExactCanonicalFingerprintBytes()
    {
        using var document = JsonDocument.Parse(NestedUnicodeInput);

        await Assert.That(JsonData.Validate(NestedUnicodeInput, new())).IsEqualTo(NestedUnicodeCanonical);
        await Assert.That(JsonData.Fingerprint(document.RootElement)).IsEqualTo(NestedUnicodeFingerprint);
    }

    [Test]
    public async Task AcMp006ValidationRejectsDuplicateDecimalDepthAndInputBudgetErrors()
    {
        var duplicate = Assert.ThrowsExactly<KeyLoadException>(() => JsonData.Validate("{\"a\":1,\"a\":2}", new()));
        using var duplicateDocument = JsonDocument.Parse("{\"a\":1,\"a\":2}");
        var fingerprintDuplicate = Assert.ThrowsExactly<KeyLoadException>(() => JsonData.Fingerprint(duplicateDocument.RootElement));
        var decimalRange = Assert.ThrowsExactly<KeyLoadException>(() => JsonData.Validate("1e1000", new(), false));
        var depth = Assert.ThrowsExactly<KeyLoadException>(() => JsonData.Validate("{\"a\":{\"b\":{\"c\":1}}}",
            new() { MaxJsonDepth = 2 }));
        var inputBytes = Assert.ThrowsExactly<KeyLoadException>(() => JsonData.Validate("{} ",
            new() { MaxDocumentBytes = 2 }));
        var requiredObject = Assert.ThrowsExactly<KeyLoadException>(() => JsonData.Validate("null", new()));

        await Assert.That(duplicate.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(fingerprintDuplicate.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(decimalRange.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(depth.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(inputBytes.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(requiredObject.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(JsonData.Validate("{}", new() { MaxDocumentBytes = 2 })).IsEqualTo("{}");
    }

    [Test]
    public async Task AcMp006ValidationRejectsNullArgumentsBeforeWork()
    {
        var missingJson = Assert.ThrowsExactly<ArgumentNullException>(() => JsonData.Validate(null!, new()));
        var missingLimits = Assert.ThrowsExactly<ArgumentNullException>(() => JsonData.Validate("{}", null!));

        await Assert.That(missingJson.ParamName).IsEqualTo("json");
        await Assert.That(missingLimits.ParamName).IsEqualTo("limits");
    }

    [Test]
    public async Task AcMp006CanonicalCommandFingerprintKeepsPersistedRetryIdempotent()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, db.Partition,
            [new PutDocument("orders", "one", "{\"z\":null,\"a\":1}")]);
        var first = db.Submit(OperationKind.Batch, command, id: id).Get<CommitReceipt>();
        var position = db.Store.Position;

        var retry = db.Submit(OperationKind.Batch, command, id: id).Get<CommitReceipt>();
        var retryPosition = db.Store.Position;
        var changed = db.Submit(OperationKind.Batch, command with
        { Mutations = [new PutDocument("orders", "two", "{\"a\":1,\"z\":null}")] }, id: id);

        await Assert.That(retry.Token).IsEqualTo(first.Token);
        await Assert.That(retryPosition).IsEqualTo(position);
        await Assert.That(db.Database.GetDocument("root", new(db.Partition, "orders", "one"))!.Json)
            .IsEqualTo("{\"z\":null,\"a\":1}");
        await Assert.That(changed.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(db.Database.GetDocument("root", new(db.Partition, "orders", "two"))).IsNull();
    }
}
