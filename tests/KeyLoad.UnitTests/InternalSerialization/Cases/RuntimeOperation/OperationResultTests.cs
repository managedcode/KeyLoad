using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class OperationResultTests
{
    private const string JsonResult = "true";
    private const string RejectedDetail = "The execution result was rejected.";

    [Test]
    public async Task StringValuesDoNotBecomeBooleanSuccess()
    {
        var result = Result<object>.Succeed(JsonResult);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => result.Get<bool>());
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task ResultRoundTripPreservesTypedSuccess()
    {
        var original = Result<object>.Succeed(true);
        var restored = OrleansSerialization.Deserialize<Result<object>>(OrleansSerialization.Serialize(original));
        await Assert.That(restored.Get<bool>()).IsTrue();
    }

    [Test]
    public async Task StoredRejectionPreservesSafeProblem()
    {
        var original = Result<object>.Fail(Errors.Problem(ErrorCode.PermissionDenied, RejectedDetail));
        var restored = OrleansSerialization.Deserialize<Result<object>>(OrleansSerialization.Serialize(original));
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => restored.Get<bool>());
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(failure.Message).IsEqualTo(RejectedDetail);
    }

    [Test]
    public async Task WrongTypeNeverBecomesTypedSuccess()
    {
        var result = Result<object>.Succeed(1);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => result.Get<bool>());
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
    }
}
