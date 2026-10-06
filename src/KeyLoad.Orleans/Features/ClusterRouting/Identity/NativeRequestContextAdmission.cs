using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Orleans;

internal static class NativeRequestContextAdmission
{
    internal static void Admit(IServiceProvider services, ClaimsPrincipal? principal,
        GrainRequestContextState state, CancellationToken cancellationToken)
    {
        var options = services.GetRequiredService<IOptions<GrainRoutingOptions>>();
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
            GrainNativeByteCounter.Measure(serializer: services.GetRequiredService<Serializer<ClaimsPrincipal>>(), value: principal,
                maximumBytes: options.Value.MaximumPrincipalBytes, cancellationToken: cancellationToken, options: options);
        }

        GrainNativeByteCounter.Measure(serializer: services.GetRequiredService<Serializer<GrainRequestContextState>>(), value: state,
            maximumBytes: options.Value.MaximumContextBytes, cancellationToken: cancellationToken, options: options);
    }
}
