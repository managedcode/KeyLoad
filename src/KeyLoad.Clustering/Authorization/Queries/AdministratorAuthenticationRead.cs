using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Core.RuntimeJournaling.Identity;

namespace KeyLoad.Clustering;

/// <summary>Uses the actual authentication capability and its fresh persisted checks under one original target budget.</summary>
internal static class AdministratorAuthenticationRead
{
    internal static AdministratorAuthenticationResult Execute(DatabaseEngine database, TimeProvider clock,
        DecodedGrainRequest request, IResourceLifetimeWorkBinding binding,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var work = new ReadExecutionBudget(clock, token);
        work.AttachResourceLifetimeBinding(binding);
        work.ConstrainLifetime(request.Envelope.ExpiresAt);
        if (!AdministratorAuthenticationScope.Validate(request.Envelope))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        var input = GrainPayload.Read<AdministratorAuthenticationRequest>(request.Payload);
        Require(input, request.Envelope.ExpiresAt);
        binding.Bind(ImmutableArray<ResourceLifetime>.Empty);
        PrincipalRecord? principal = null;
        KeyLoadException? refusal = null;
        try
        {
            principal = database.Authenticate(input.Secret, clock.GetUtcNow(), work);
            RuntimeJournalIdentity.RequirePublicCredential(principal.Id);
            GrainRequestAuthority.RequireAdministrator(principal);
        }
        catch (KeyLoadException error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            refusal = error;
        }
        var reply = new AdministratorAuthenticationResult(refusal is null ? principal : null,
            refusal?.Code, refusal?.Message);
        work.Check();
        return reply;
    }

    private static void Require(AdministratorAuthenticationRequest input, DateTimeOffset expiry)
    {
        if (string.IsNullOrEmpty(input.Secret) || input.ExpiresAt != expiry)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
    }
}
