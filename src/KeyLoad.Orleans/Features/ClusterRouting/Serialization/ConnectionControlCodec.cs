using KeyLoad.Core;

namespace KeyLoad.Orleans;

internal static class ConnectionControlCodec
{
    internal static string Create(DatabaseEngine database, TimeProvider clock,
        GrainRoutingOptions settings, Guid connectionId)
    {
        if (connectionId == Guid.Empty)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
        return database.Sign(new GrainConnectionCloseControl(ConnectionControlProtocol.Purpose,
            database.Store.Identity.Incarnation, connectionId, clock.GetUtcNow() + settings.RequestLifetime));
    }

    internal static void Verify(DatabaseEngine database, TimeProvider clock,
        GrainRoutingOptions settings, string signedControl, Guid connectionId)
    {
        var control = database.Verify<GrainConnectionCloseControl>(
            signedControl, ConnectionControlProtocol.MaximumTokenCharacters);
        var now = clock.GetUtcNow();
        if (connectionId == Guid.Empty || control.ConnectionId != connectionId
            || control.Purpose != ConnectionControlProtocol.Purpose
            || control.Incarnation != database.Store.Identity.Incarnation
            || control.ExpiresAt <= now || control.ExpiresAt > now + settings.MaximumFuture)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
    }
}
