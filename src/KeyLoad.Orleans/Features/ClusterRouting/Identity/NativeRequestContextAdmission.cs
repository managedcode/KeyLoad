using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Orleans;

internal static class NativeRequestContextAdmission
{
    private const int EmptyContextBytes = 0;

    internal static void Admit(IServiceProvider services, ClaimsPrincipal? principal,
        GrainRequestContextState state, CancellationToken cancellationToken, SiloAddress? placement = null)
    {
        var options = services.GetRequiredService<IOptions<GrainRoutingOptions>>();
        cancellationToken.ThrowIfCancellationRequested();
        if (state.RequestId == Guid.Empty || state.ConnectionId == Guid.Empty)
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

        var stateBytes = GrainNativeByteCounter.Measure(serializer: services.GetRequiredService<Serializer<GrainRequestContextState>>(), value: state,
            maximumBytes: options.Value.MaximumContextBytes, cancellationToken: cancellationToken, options: options);
        if (placement is not null)
        {
            var remaining = checked(options.Value.MaximumContextBytes - (int)stateBytes);
            if (remaining <= EmptyContextBytes)
            { throw Errors.Fail(ErrorCode.BudgetExceeded, GrainRoutingProtocol.ReplyBudgetExceeded); }
            GrainNativeByteCounter.Measure(services.GetRequiredService<Serializer<SiloAddress>>(), placement,
                remaining, options, cancellationToken);
        }
    }
}
