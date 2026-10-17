using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class NativeStreamTests
{
    private const string WriteFailure = "The fixture stream rejected its write.";

    [Test]
    public async Task AcIs002StreamUsesTheSameExactNativePayloadAsTheArrayApi()
    {
        var value = new OutboxHead(1, 1, 1, 1);
        using var destination = new MemoryStream();
        NativeSerialization.Serialize(value, destination);
        await Assert.That(destination.ToArray()).IsEquivalentTo(NativeSerialization.Serialize(value), CollectionOrdering.Matching);
        await Assert.That(destination.CanWrite).IsTrue();
        await Assert.That(destination.Length).IsEqualTo(NativeSerialization.Measure(value));
    }

    [Test]
    public async Task AcIs002StreamWriteFailurePropagatesUnchanged()
    {
        var expected = new IOException(WriteFailure);
        using var destination = new NativeFailureStream(expected);
        var actual = Capture(() => NativeSerialization.Serialize(new OutboxHead(1, 1, 1, 1), destination));
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async Task AcIs002StreamCallerCancellationPropagatesItsToken()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var destination = new NativeFailureStream(new IOException(WriteFailure), cancellation.Token);
        var actual = Capture(() => NativeSerialization.Serialize(new OutboxHead(1, 1, 1, 1), destination));
        await Assert.That(actual.GetType()).IsEqualTo(typeof(OperationCanceledException));
        await Assert.That(((OperationCanceledException)actual).CancellationToken).IsEqualTo(cancellation.Token);
    }

    [Test]
    public async Task AcIs002StreamRejectsMissingRootAndDestination()
    {
        using var destination = new MemoryStream();
        await Assert.That(((KeyLoadException)Capture(() => NativeSerialization.Serialize<object>(null!, destination))).Code)
            .IsEqualTo(ErrorCode.Corruption);
        await Assert.That(Capture(() => NativeSerialization.Serialize(new OutboxHead(1, 1, 1, 1), null!)).GetType())
            .IsEqualTo(typeof(ArgumentNullException));
        await Assert.That(destination.Length).IsEqualTo(0L);
    }

    private static Exception Capture(Action action)
    {
        try
        {
            action();
        }
        catch (KeyLoadException exception)
        {
            return exception;
        }
        catch (IOException exception)
        {
            return exception;
        }
        catch (OperationCanceledException exception)
        {
            return exception;
        }
        catch (ArgumentNullException exception)
        {
            return exception;
        }
        throw new InvalidOperationException(NativeContractCases.ExpectedFailure);
    }
}
