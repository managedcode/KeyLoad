namespace KeyLoad.UnitTests.ClientApi;

internal sealed record SuccessBodyDelivery(bool Command, int Status, long? DeclaredBytes, int WrittenBytes);
