using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class PostgresRetainedAdmission(IOptions<PostgresSessionOptions> options)
{
    private const string Exhausted = "The native SQL retained-session byte grant is exhausted.";
    private readonly Lock gate = new();
    private long reserved;

    internal void Resize(int previous, int next)
    {
        lock (gate)
        {
            var replacement = checked(reserved - previous + next);
            if (replacement > options.Value.MaximumTotalRetainedBytes)
            { throw Errors.Fail(ErrorCode.ResourceExhausted, Exhausted); }
            reserved = replacement;
        }
    }
}
