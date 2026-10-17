namespace KeyLoad.Clustering;

public sealed partial class GrainRequestCodec
{
    internal RemoteGraphRequestCodec RemoteGraph => new(this, database, clock, settings.RequestLifetime);
}
