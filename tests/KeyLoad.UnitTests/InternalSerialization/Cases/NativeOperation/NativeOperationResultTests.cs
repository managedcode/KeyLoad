using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class NativeOperationResultTests
{
    private const string JsonResult = "true";
    private const string RejectedDetail = "The native result was rejected.";

    [Test]
    public async Task StringValuesDoNotBecomeBooleanSuccess()
    {
        var result = Result<object>.Succeed(JsonResult);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => result.Get<bool>());
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task NativeResultRoundTripPreservesTypedSuccess()
    {
        var original = Result<object>.Succeed(true);
        var restored = NativeSerialization.Deserialize<Result<object>>(NativeSerialization.Serialize(original));
        await Assert.That(restored.Get<bool>()).IsTrue();
    }

    [Test]
    public async Task StoredNativeRejectionPreservesSafeProblem()
    {
        var original = Result<object>.Fail(Errors.Problem(ErrorCode.PermissionDenied, RejectedDetail));
        var restored = NativeSerialization.Deserialize<Result<object>>(NativeSerialization.Serialize(original));
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => restored.Get<bool>());
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(failure.Message).IsEqualTo(RejectedDetail);
    }

    [Test]
    public async Task WrongNativeTypeNeverBecomesTypedSuccess()
    {
        var result = Result<object>.Succeed(1);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => result.Get<bool>());
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
    }
}
