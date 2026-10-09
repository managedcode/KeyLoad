namespace KeyLoad.Server;

internal sealed partial class OrleansNode
{
    internal Task CloseConnectionAsync(Guid connectionId, CancellationToken cancellationToken)
        => OrleansNodeRequestExecutor.CloseAsync(Grains ?? throw Errors.Fail(ErrorCode.OwnershipLost, OrleansNodeProtocol.RoutingUnavailable),
            RuntimeServices, connectionId, cancellationToken);
}
