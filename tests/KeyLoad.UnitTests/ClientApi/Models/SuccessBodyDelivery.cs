namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed record SuccessBodyDelivery(bool Command, int Status, long? DeclaredBytes, int WrittenBytes);
