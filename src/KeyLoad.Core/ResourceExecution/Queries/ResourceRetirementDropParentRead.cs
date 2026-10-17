using System.Text.Json;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal ResourceRetirementDropParentV1? ReadResourceRetirementDropParent(string principalId,
        ResourceRetirementDropParentLookupV1 lookup, ReadExecutionBudget work)
    {
        work.Check();
        var parent = Store.Read(raw =>
        {
            var view = work.CreateView(raw);
            var principal = RequireInventoryAdministrator(view, principalId);
            return ReadRetirementDropParent(view, lookup, principal.Id);
        });
        work.CheckResult(parent);
        work.Check();
        return parent;
    }

    private ResourceRetirementDropParentV1? ReadRetirementDropParent(IKeyValueView view,
        ResourceRetirementDropParentLookupV1 lookup, string principal)
    {
        var parent = ResourceRetirementDropParentStorage.Read(view, lookup, principal, Limits);
        if (parent is null)
        {
            return null;
        }
        RequireRetirementDropParentShape(parent);
        RequireRetirementDropParentOutcome(view, parent, completion: false);
        if (parent.OriginalCompletion is not null)
        {
            RequireRetirementDropParentOutcome(view, parent, completion: true);
            var state = ResourceRetirementStorage.ReadControl(view, parent.RetirementRequest, Limits)
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Missing);
            var proven = RequireResourceRetirementFinalizedSource(view, state.Control);
            if (!ResourceRetirementValidation.Same(proven, state))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
            ResourceRetirementCompletionValidation.RequireCompleted(proven, Limits);
            var canonical = RequireRetirementDropCompletion(parent, proven);
            var result = ReadRetirementDropResult(parent.ResultJson!);
            if (result is null || result.TenantId != parent.Lookup.Request.TenantId
                || result.DatabaseId != parent.Lookup.Request.DatabaseId || result.ResourceName != parent.Lookup.Request.ResourceName
                || !ResourceRetirementValidation.Same(result.Receipt, canonical))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
        }
        return parent;
    }

    private static DropTableResult? ReadRetirementDropResult(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<DropTableResult>(json, JsonDefaults.Options);
        }
        catch (JsonException)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
    }

    private static string RetirementDropFingerprint(ResourceRetirementDropParentLookupV1 lookup, string principal)
        => NativeOperationFingerprint.Compute(lookup.CommandId, OperationKind.DropTable, principal,
            JsonSerializer.SerializeToUtf8Bytes(lookup.Request, JsonDefaults.Options));

    private void RequireRetirementDropParentShape(ResourceRetirementDropParentV1 parent)
    {
        if (parent.OriginalIntent is null || parent.PersistReceipt?.PhysicalOwner is null
            || parent.Definition is null || parent.Lifetime is null || parent.PrincipalId is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        var request = parent.RetirementRequest;
        ResourceRetirementValidation.Request(request, Limits);
        ResourceLifetimeIdentity.Validate(parent.Lifetime, request.TenantId, request.DatabaseId, parent.Definition);
        if (parent.Version != ResourceRetirementProtocol.Version || parent.PolicyEpoch < ResourceRetirementProtocol.Empty
            || parent.CompletionPolicyEpoch < ResourceRetirementProtocol.Empty
            || parent.PublicFingerprint != RetirementDropFingerprint(parent.Lookup, parent.PrincipalId)
            || request.RetirementId != parent.Lookup.CommandId || request.TenantId != parent.Lookup.Request.TenantId
            || request.DatabaseId != parent.Lookup.Request.DatabaseId || request.ResourceName != parent.Lookup.Request.ResourceName
            || request.ExpectedIncarnation != parent.Lifetime.Incarnation || parent.Definition.Name != request.ResourceName
            || (parent.ResultJson is null) != (parent.OriginalCompletion is null)
            || (parent.ResultJson is null) != (parent.CompleteReceipt is null)
            || (parent.ResultJson is null) != (parent.CompletionPolicyEpoch is null))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        ResourceRetirementValidation.Bound(parent, Limits);
    }
}
