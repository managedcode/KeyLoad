namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class NativeOperationResultTests
{
    private const string JsonResult = "true";
    private const string RejectedDetail = "The native result was rejected.";

    [Test]
    public async Task AcIs001And003JsonOnlyInternalResultsNeverBecomeTypedSuccess()
    {
        var result = new OperationResult(JsonResult);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => result.Get<bool>());
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcIs003NativeResultRoundTripPreservesTypedSuccessWithoutJson()
    {
        var original = new OperationResult(null) { NativeValue = true };
        var restored = NativeSerialization.Deserialize<OperationResult>(NativeSerialization.Serialize(original));
        await Assert.That(restored.Json).IsNull();
        await Assert.That(restored.Get<bool>()).IsTrue();
    }

    [Test]
    public async Task AcIs003StoredRejectionCannotBeMaskedByNativeOrJsonValue()
    {
        var original = new OperationResult(JsonResult, ErrorCode.PermissionDenied, RejectedDetail) { NativeValue = true };
        var restored = NativeSerialization.Deserialize<OperationResult>(NativeSerialization.Serialize(original));
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => restored.Get<bool>());
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(failure.Message).IsEqualTo(RejectedDetail);
    }

    [Test]
    public async Task AcIs003WrongNativeTypeNeverFallsBackToJsonResult()
    {
        var result = new OperationResult(JsonResult) { NativeValue = 1 };
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => result.Get<bool>());
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
    }
}
