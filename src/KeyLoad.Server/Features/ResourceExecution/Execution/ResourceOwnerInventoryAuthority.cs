using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ResourceExecution;

/// <summary>Captures every registered owner before revalidation; completion describes a captured cut, not retirement.</summary>
internal sealed class ResourceOwnerInventoryAuthority(PartitionHost partition, ResourceOwnerInventoryNativeClient client,
    IOptions<NodeOptions> nodeOptions, IOptions<DatabaseLimits> limits, TimeProvider clock)
{
    private readonly ResourceOwnerInventoryNativeRead reads = new(client);

    internal async Task<ResourceOwnerInventoryResultV1> ExecuteAsync(string sourcePrincipalId, long sourcePolicyEpoch,
        ResourceOwnerInventoryRequestV1 request, ReadExecutionBudget work, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(work);
        token.ThrowIfCancellationRequested();
        if (request.ExpiresAt <= clock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.CursorExpired, ResourceOwnerInventoryNativeProtocol.Unavailable);
        }
        work.ConstrainLifetime(request.ExpiresAt);
        using var cancellation = work.EnterStageCancellation(token);
        var configuredControl = PhysicalOwnerConfiguredTuples.Control(nodeOptions.Value, partition);
        var initial = await reads.ExecuteAsync(true, ResourceOwnerInventoryReadOperation.CaptureControl, request,
            null, configuredControl, null, null, null, sourcePrincipalId, sourcePolicyEpoch, work, token).ConfigureAwait(false);
        var control = initial.Control!;
        if (control.Request != request || control.PrincipalId != sourcePrincipalId || control.PolicyEpoch != sourcePolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceOwnerInventoryNativeProtocol.Unavailable);
        }
        if (!PhysicalOwnerEntryValidation.SameOwner(control.Directory.ControlOwner, configuredControl.Owner)
            || control.Directory.Owners.IsDefaultOrEmpty)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ResourceOwnerInventoryNativeProtocol.Unavailable);
        }
        var capture = new ResourceOwnerInventoryCapture(control, limits.Value.MaxResults);
        capture.Initialize(work);
        var owners = await CaptureAllAsync(control, capture, sourcePrincipalId, sourcePolicyEpoch, work, token)
            .ConfigureAwait(false);
        foreach (var (owner, voter, cut) in owners.OrderByDescending(value =>
            PhysicalOwnerEntryValidation.SameOwner(value.Owner.Owner, control.Directory.ControlOwner))
            .ThenBy(value => value.Owner.Owner.VoterIds.IndexOf(value.Voter)))
        {
            _ = await reads.ExecuteAsync(false, ResourceOwnerInventoryReadOperation.ValidateOwner, request, control,
                owner, voter, null, cut, sourcePrincipalId, sourcePolicyEpoch, work, token).ConfigureAwait(false);
        }
        _ = await reads.ExecuteAsync(true, ResourceOwnerInventoryReadOperation.ValidateControl, request, control,
            configuredControl, null, null, null, sourcePrincipalId, sourcePolicyEpoch, work, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return capture.Complete(work);
    }

    private async Task<List<(RegisteredPhysicalOwnerV1 Owner, string Voter, ResourceOwnerInventoryOwnerCutV1 Cut)>>
        CaptureAllAsync(ResourceOwnerInventoryControlV1 control, ResourceOwnerInventoryCapture capture,
            string principalId, long policyEpoch, ReadExecutionBudget work, CancellationToken token)
    {
        var owners = new List<(RegisteredPhysicalOwnerV1 Owner, string Voter, ResourceOwnerInventoryOwnerCutV1 Cut)>();
        foreach (var owner in control.Directory.Owners)
        {
            foreach (var voter in owner.Owner.VoterIds)
            {
                var cut = await CaptureOwnerAsync(control, owner, voter, capture, principalId, policyEpoch, work, token)
                    .ConfigureAwait(false);
                owners.Add((owner, voter, cut));
            }
        }
        return owners;
    }

    private async Task<ResourceOwnerInventoryOwnerCutV1> CaptureOwnerAsync(ResourceOwnerInventoryControlV1 control,
        RegisteredPhysicalOwnerV1 owner, string voter, ResourceOwnerInventoryCapture capture, string principalId, long policyEpoch,
        ReadExecutionBudget work, CancellationToken token)
    {
        ResourceOwnerInventoryCursorV1? cursor = null;
        ResourceOwnerInventoryOwnerCutV1? originalCut = null;
        do
        {
            var reply = await reads.ExecuteAsync(false, ResourceOwnerInventoryReadOperation.ReadPage, control.Request,
                control, owner, voter, cursor, null, principalId, policyEpoch, work, token).ConfigureAwait(false);
            var page = reply.Page!;
            capture.AddPage(owner, page, originalCut, work);
            originalCut ??= page.Cut;
            if (page.Continuation is { } next)
            {
                capture.RequireContinuation(cursor, next, originalCut);
            }
            cursor = page.Continuation;
        }
        while (cursor is not null);
        var completedCut = originalCut
            ?? throw Errors.Fail(ErrorCode.Corruption, ResourceOwnerInventoryNativeProtocol.Unavailable);
        capture.AddCut(completedCut, work);
        return completedCut;
    }
}
