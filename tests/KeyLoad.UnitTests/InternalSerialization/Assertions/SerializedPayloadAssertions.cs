namespace KeyLoad.UnitTests.InternalSerialization;

internal static class SerializedPayloadAssertions
{
    internal static async Task CorruptAsync(Action operation)
    {
        KeyLoadException? failure = null;
        try
        {
            operation();
        }
        catch (KeyLoadException exception)
        {
            failure = exception;
        }
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(failure.Message).IsEqualTo(PayloadVersion.InvalidPayload);
    }
}
