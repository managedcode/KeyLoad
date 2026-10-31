using KeyLoad.Clustering;
using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Authenticates the administrator through the same execution request-grain entry.</summary>
internal static class AdministratorAuthenticationRequests
{
    internal static Task<PrincipalRecord> StartupAsync(OrleansNode node, string secret,
        IOptions<McpExecutionOptions> options, CancellationToken token)
    {
        var work = new ReadExecutionBudget(node.CatalogRequestCodec().RuntimeClock, token);
        return AuthenticateAsync(node, secret, options, work, token);
    }

    internal static async Task<PrincipalRecord> AuthenticateAsync(OrleansNode node, string secret,
        IOptions<McpExecutionOptions> options, ReadExecutionBudget work, CancellationToken token)
    {
        work.Check();
        var requestId = Guid.NewGuid();
        var codec = node.CatalogRequestCodec();
        var envelope = codec.CreateAdministratorAuthentication(requestId,
            new AdministratorAuthenticationRequest(secret));
        var dispatched = false;
        AdministratorAuthenticationResult? value = null;
        try
        {
            var reply = await AdministratorAuthenticationDispatch.SendAsync(node, requestId,
                envelope, () => dispatched = true, token);
            value = GrainReplyFactory.RequireValue(reply).Value as AdministratorAuthenticationResult
                ?? throw Errors.Fail(ErrorCode.Corruption, AdministratorAuthenticationProtocol.Invalid);
        }
        catch (Exception original) when (dispatched && CqrsBoundaryErrors.IsNonFatal(original))
        {
            work.FailIndeterminateMetadata(original);
            throw;
        }
        Require(value);
        if (value.Error is { } denied)
        {
            throw Errors.Fail(denied, value.SafeDetail!);
        }
        work.Check();
        var principal = McpAuthentication.ReadPrincipal(new GrainValue(value.Principal), options, token);
        GrainRequestAuthority.RequireAdministrator(principal);
        work.Check();
        return principal;
    }

    private static void Require(AdministratorAuthenticationResult value)
    {
        if (value.Error is { } error && (!Enum.IsDefined(error) || value.Principal is not null
                || string.IsNullOrEmpty(value.SafeDetail))
            || value.Error is null && (value.Principal is null || value.SafeDetail is not null))
        {
            throw Errors.Fail(ErrorCode.Corruption, AdministratorAuthenticationProtocol.Invalid);
        }
    }
}
