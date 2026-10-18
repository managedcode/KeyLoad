namespace KeyLoad.Clustering;

public sealed partial class GrainRequestCodec
{
    /// <summary>Issue a bounded close control for a server-owned connection.</summary>
    /// <param name="connectionId">The server-issued execution owner identity.</param>
    /// <returns>The signed, expiring control.</returns>
    public string CreateConnectionClose(Guid connectionId)
    {
        if (connectionId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        return Sign(new GrainConnectionCloseControl(ConnectionControlProtocol.Purpose,
            Incarnation, connectionId, clock.GetUtcNow() + settings.RequestLifetime));
    }

    /// <summary>Verify a close control against the current connection and database.</summary>
    /// <param name="signedControl">The server-issued bounded token.</param>
    /// <param name="connectionId">The actual grain owner identity.</param>
    public void VerifyConnectionClose(string signedControl, Guid connectionId)
    {
        var control = VerifySigned<GrainConnectionCloseControl>(
            signedControl, ConnectionControlProtocol.MaximumTokenCharacters);
        var now = clock.GetUtcNow();
        if (connectionId == Guid.Empty || control.ConnectionId != connectionId
            || control.Purpose != ConnectionControlProtocol.Purpose
            || control.Incarnation != Incarnation
            || control.ExpiresAt <= now || control.ExpiresAt > now + settings.MaximumFuture)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
    }
}
