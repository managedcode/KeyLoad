using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

internal static class AdministratorAuthenticationEncoding
{
    internal static ReadOnlyMemory<byte> Principal(PrincipalRecord principal, ReadExecutionBudget work,
        IOptions<GrainRoutingOptions> options, CancellationToken token)
    {
        work.CheckResult(principal);
        _ = ResultByteCounterStream.RuntimeMeasure(work, new GrainValue(principal));
        using var stream = new GrainBoundedPayloadStream(Math.Min(work.MaximumResultBytes,
            options.Value.MaximumReplyBytes), options, token);
        OrleansSerialization.Serialize(new GrainValue(principal), stream);
        work.Check();
        return stream.Complete();
    }
}
