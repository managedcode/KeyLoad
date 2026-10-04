using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.Orleans;

internal static class NativeRequestContextAdmission
{
    internal static void Admit(IServiceProvider services, ClaimsPrincipal? principal,
        GrainRequestContextState state, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (state.RequestId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }

        if (principal is not null)
        {
            var subject = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? throw Errors.Fail(ErrorCode.Unauthenticated, GrainRoutingProtocol.MissingPrincipal);
            GrainIdentityContext.ValidatePrincipal(principal, subject);
            GrainNativeByteCounter.Measure(services.GetRequiredService<Serializer<ClaimsPrincipal>>(), principal,
                GrainRequestStreamProtocol.MaximumPrincipalBytes, cancellationToken);
        }

        GrainNativeByteCounter.Measure(services.GetRequiredService<Serializer<GrainRequestContextState>>(), state,
            GrainRequestStreamProtocol.MaximumContextBytes, cancellationToken);
    }
}
