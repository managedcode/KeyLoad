namespace KeyLoad.Server.Features.DocumentStorage;

// Local owned proof; it is never serialized or persisted.
internal sealed record RemoteModelAuthenticatedReply(ReadOnlyMemory<byte> Payload, string Signature,
    string DispatchSiloAddress)
{
    internal bool MayHaveEffects
    {
        get; init;
    }
}

