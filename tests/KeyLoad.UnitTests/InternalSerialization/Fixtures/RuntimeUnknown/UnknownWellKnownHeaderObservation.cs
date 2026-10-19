using Orleans.Serialization.WireProtocol;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed record UnknownWellKnownHeaderObservation(Field? Header, Exception? Error,
    long Position, long Remaining, uint ObjectReference, Type? FirstTypeReference, bool SecondTypeReference);
