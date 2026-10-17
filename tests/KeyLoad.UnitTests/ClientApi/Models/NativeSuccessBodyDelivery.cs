namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed record NativeSuccessBodyDelivery(bool Command, int Status, long? DeclaredBytes, int WrittenBytes);
