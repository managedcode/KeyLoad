using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Borrows the caller's original numeric read grant through the same native request-grain entry.</summary>
internal static class NativeAdministratorAuthenticationRequests
{
    private const long NoReadBytes = 0L;
    private const int NoExaminedRecords = 0;
    private const int NoResultBytes = 0;
    internal static Task<PrincipalRecord> StartupAsync(OrleansNode node, string secret,
        IOptions<McpExecutionOptions> options, CancellationToken token)
    {
        var context = node.CatalogRequestCodec().LifetimeReadContext;
        var work = new ReadExecutionBudget(context.Scope.OperationLimits,
            context.Scope.RuntimeClock, token);
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
            throw Errors.Fail(ErrorCode.BudgetExceeded, NativeAdministratorAuthenticationProtocol.BudgetExceeded);
        }
        var grant = work.CreateReadGrant(bytes, records);
        var failures = new List<Exception>();
        PrincipalRecord? principal = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            principal = await AuthenticateAsync(node, secret, options, work, grant,
                originalExpiry, token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
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
            throw Errors.Fail(ErrorCode.Validation, NativeAdministratorAuthenticationProtocol.Invalid);
        }
        work.Check();
        var bytes = metadataGrant.RemainingBytes;
        var records = metadataGrant.RemainingRecords;
        var resultBytes = work.MaximumResultBytes;
        if (bytes <= NoReadBytes || records <= NoExaminedRecords || resultBytes <= NoResultBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, NativeAdministratorAuthenticationProtocol.BudgetExceeded);
        }
        var failures = new List<Exception>();
        PrincipalRecord? principal = null;
        var dispatched = false;
        var imported = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var requestId = Guid.NewGuid();
            var codec = node.CatalogRequestCodec();
            var input = new NativeAdministratorAuthenticationRequestV1(secret, bytes, records, resultBytes, originalExpiry);
            var signed = codec.CreateNativeAdministratorAuthentication(requestId, input);
            var reply = await NativeAdministratorAuthenticationDispatch.SendAsync(node, requestId,
                signed, () => dispatched = true, token).ConfigureAwait(false);
            if (reply.Payload.IsEmpty || reply.Payload.Length > resultBytes)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, NativeAdministratorAuthenticationProtocol.BudgetExceeded);
            }
            // Native decoding validates the full bounded syntax before materializing the outer byte payload.
            var value = GrainNativePayload.Read<GrainValue>(reply.Payload).Value
                as NativeAdministratorAuthenticationResultV1
                ?? throw Errors.Fail(ErrorCode.Corruption, NativeAdministratorAuthenticationProtocol.Invalid);
            Require(value, codec.ExecutionOptions.Value.MaximumDetailCharacters);
            work.ImportSettledReadGrant(metadataGrant, value.ReadBytes, value.ExaminedRecords);
            imported = true;
            if (value.Error is { } denied)
            {
                throw Errors.Fail(denied, value.SafeDetail!);
            }
            work.Check();
            // The inner wire remains exactly the old principal grammar, admitted before retained principal decode.
            principal = McpNativeAuthentication.ReadPrincipal(value.PrincipalPayload.Span, options, token);
            GrainRequestAuthority.RequireAdministrator(principal);
            work.CheckResult(principal);
            work.Check();
        }, failures).ConfigureAwait(false);
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

    private static void Require(NativeAdministratorAuthenticationResultV1 value, int maximumDetailCharacters)
    {
        if (value.ReadBytes < NoReadBytes || value.ExaminedRecords < NoExaminedRecords
            || value.Error is { } error && (!Enum.IsDefined(error) || !value.PrincipalPayload.IsEmpty
                || string.IsNullOrEmpty(value.SafeDetail) || value.SafeDetail.Length > maximumDetailCharacters)
            || value.Error is null && (value.PrincipalPayload.IsEmpty || value.SafeDetail is not null))
        {
            throw Errors.Fail(ErrorCode.Corruption, NativeAdministratorAuthenticationProtocol.Invalid);
        }
    }
}
