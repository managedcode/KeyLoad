namespace KeyLoad.Server;

/// <summary>Carries the actual request-actor identity with its canonical owned reply bytes.</summary>
/// <param name="RequestId">The fresh Orleans request GUID, distinct from the command identity.</param>
/// <param name="Payload">The exact bounded canonical UTF-8 response.</param>
internal readonly record struct DispatchedOperationReply(Guid RequestId, ReadOnlyMemory<byte> Payload);
