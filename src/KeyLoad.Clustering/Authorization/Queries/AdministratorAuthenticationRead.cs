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
        ArgumentNullException.ThrowIfNull(binding) /* KLD0041: BCL null validation accepts the binding IResourceLifetimeWorkBinding reference through its object parameter. */;
        var work = new ReadExecutionBudget(token);
        work.AttachResourceLifetimeBinding(binding);
        if (!AdministratorAuthenticationScope.Validate(request.Envelope))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        var input = (GrainPayload.Read<AdministratorAuthenticationRequest>((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as KeyLoad.Clustering.AdministratorAuthenticationRequest at this closed operation boundary before executing typed code. */);
        Require(input);
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

    private static void Require(AdministratorAuthenticationRequest input)
    {
        if (string.IsNullOrEmpty(input.Secret))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
    }
}
