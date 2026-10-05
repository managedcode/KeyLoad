using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

internal sealed class ReplicaEnvelopeFreshness(TimeProvider clock, IOptions<ReplicaTransportOptions> options)
{
    private readonly ReplicaTransportOptions settings = options.Value;

    internal long Now() => clock.GetUtcNow().ToUnixTimeMilliseconds();

    internal bool Includes(long timestamp)
    {
        var lifetime = checked((long)settings.EnvelopeLifetime.TotalMilliseconds);
        var now = Now();
        return timestamp >= now - lifetime && timestamp <= now + lifetime;
    }
}
