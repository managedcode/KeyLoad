using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution.Execution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Borrows the caller's original numeric read grant through the same execution request-grain entry.</summary>
internal static class AdministratorAuthenticationRequests
{
    private const long NoReadBytes = 0L;
    private const int NoExaminedRecords = 0;
    private const int NoResultBytes = 0;
    internal static Task<PrincipalRecord> StartupAsync(OrleansNode node, string secret,
        IOptions<McpExecutionOptions> options, CancellationToken token)
    {
        var context = node.CatalogRequestCodec().LifetimeReadContext;
        var work = new ReadExecutionBudget(context.Scope.RuntimeClock, token);
        var expiry = context.Now + context.Lifetime;
        work.ConstrainLifetime(expiry);
        return AuthenticateAsync(node, secret, options, work, expiry, token);
    }

    internal static async Task<PrincipalRecord> AuthenticateAsync(OrleansNode node, string secret,
        IOptions<McpExecutionOptions> options, ReadExecutionBudget work, DateTimeOffset originalExpiry,
        CancellationToken token)
    {
        work.Check();
        var bytes = work.RemainingReadGrantBytes;
        var records = work.RemainingReadGrantRecords;
        if (bytes <= NoReadBytes || records <= NoExaminedRecords || work.MaximumResultBytes <= NoResultBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, AdministratorAuthenticationProtocol.BudgetExceeded);
        }
        var grant = work.CreateReadGrant(bytes, records);
        var failures = new List<Exception>();
        PrincipalRecord? principal = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            principal = await AuthenticateAsync(node, secret, options, work, grant,
                originalExpiry, token);
        }, failures);
        ServerFailureObserver.Observe(() => work.CompleteSettledReadGrant(grant), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return principal!;
    }

    internal static async Task<PrincipalRecord> AuthenticateAsync(OrleansNode node, string secret,
        IOptions<McpExecutionOptions> options, ReadExecutionBudget work,
        ReadExecutionBudgetReadGrant metadataGrant, DateTimeOffset originalExpiry, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(metadataGrant);
        if (!metadataGrant.BelongsTo(work) || metadataGrant.IsCompleted)
        {
            throw Errors.Fail(ErrorCode.Validation, AdministratorAuthenticationProtocol.Invalid);
        }
        work.Check();
        var bytes = metadataGrant.RemainingBytes;
        var records = metadataGrant.RemainingRecords;
        var resultBytes = work.MaximumResultBytes;
        if (bytes <= NoReadBytes || records <= NoExaminedRecords || resultBytes <= NoResultBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, AdministratorAuthenticationProtocol.BudgetExceeded);
        }
        var failures = new List<Exception>();
        PrincipalRecord? principal = null;
        var dispatched = false;
        var imported = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var requestId = Guid.NewGuid();
            var codec = node.CatalogRequestCodec();
            var input = new AdministratorAuthenticationRequest(secret, bytes, records, resultBytes, originalExpiry);
            var envelope = codec.CreateAdministratorAuthentication(requestId, input);
            var reply = await AdministratorAuthenticationDispatch.SendAsync(node, requestId,
                envelope, () => dispatched = true, token);
            if (reply.Payload.IsEmpty || reply.Payload.Length > resultBytes)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, AdministratorAuthenticationProtocol.BudgetExceeded);
            }
            // Execution decoding validates the full bounded syntax before materializing the outer byte payload.
            var value = GrainPayload.Read<GrainValue>(reply.Payload).Value
                as AdministratorAuthenticationResult
                ?? throw Errors.Fail(ErrorCode.Corruption, AdministratorAuthenticationProtocol.Invalid);
            Require(value, codec.ExecutionOptions.Value.MaximumDetailCharacters);
            work.ImportSettledReadGrant(metadataGrant, value.ReadBytes, value.ExaminedRecords);
            imported = true;
            if (value.Error is { } denied)
            {
                throw Errors.Fail(denied, value.SafeDetail!);
            }
            work.Check();
            // The inner wire remains exactly the old principal grammar, admitted before retained principal decode.
            principal = McpAuthentication.ReadPrincipal(value.PrincipalPayload.Span, options, token);
            GrainRequestAuthority.RequireAdministrator(principal);
            work.CheckResult(principal);
            work.Check();
        }, failures);
        try
        {
            ServerFailureObserver.ThrowIfAny(failures);
        }
        catch (Exception original)
        {
            if (dispatched && !imported)
            {
                work.FailIndeterminateMetadata(original);
            }
            throw;
        }
        return principal!;
    }

    private static void Require(AdministratorAuthenticationResult value, int maximumDetailCharacters)
    {
        if (value.ReadBytes < NoReadBytes || value.ExaminedRecords < NoExaminedRecords
            || value.Error is { } error && (!Enum.IsDefined(error) || !value.PrincipalPayload.IsEmpty
                || string.IsNullOrEmpty(value.SafeDetail) || value.SafeDetail.Length > maximumDetailCharacters)
            || value.Error is null && (value.PrincipalPayload.IsEmpty || value.SafeDetail is not null))
        {
            throw Errors.Fail(ErrorCode.Corruption, AdministratorAuthenticationProtocol.Invalid);
        }
    }
}
