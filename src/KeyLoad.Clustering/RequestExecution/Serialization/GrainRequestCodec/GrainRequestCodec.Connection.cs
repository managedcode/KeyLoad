namespace KeyLoad.Clustering;

public sealed partial class GrainRequestCodec
{
    /// <summary>Creates the expiring close control bound to this database and one server-owned connection.</summary>
    /// <param name="connectionId">The server-issued connection identity.</param>
    /// <returns>The scoped close control.</returns>
    public GrainConnectionCloseControl CreateConnectionClose(Guid connectionId)
    {
        if (connectionId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        return new GrainConnectionCloseControl(ConnectionControlProtocol.Purpose,
            Incarnation, connectionId, clock.GetUtcNow() + settings.RequestLifetime);
    }

    internal void ValidateConnectionClose(GrainConnectionCloseControl control, Guid connectionId)
    {
        ArgumentNullException.ThrowIfNull(control);
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
