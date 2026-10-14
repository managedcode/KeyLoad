namespace KeyLoad.Clustering;

public sealed partial class GrainRequestCodec
{
    internal PartitionMovementTransferReadCodec MovementTransfer
        => new(this, database, clock, settings.RequestLifetime);
}
