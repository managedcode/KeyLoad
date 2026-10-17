using Orleans.Serialization.WireProtocol;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed record NativeUnknownWellKnownHeaderObservation(Field? Header, Exception? Error,
    long Position, long Remaining, uint ObjectReference, Type? FirstTypeReference, bool SecondTypeReference);
