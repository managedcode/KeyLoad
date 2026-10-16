using KeyLoad.Core;
using KeyLoad.Core.Features.DocumentStorage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

internal static class DocumentLifetimeReadCapabilities
{
    private const int Empty = 0;
    private const long EmptyBytes = 0;

    internal static object Execute(DatabaseEngine database, IServiceProvider services, TimeProvider clock,
        PrincipalRecord principal, DecodedGrainRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!DocumentLifetimeReadScope.Validate(request.Envelope))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        var limits = services.GetRequiredService<IOptions<DatabaseLimits>>();
        return request.Envelope.ReadKind switch
        {
            GrainReadKind.OwnedDocumentV2 => Owned(database, limits, clock, principal, request.Envelope,
                GrainNativePayload.Read<OwnedDocumentReadRequestV2>(request.Payload), token),
            GrainReadKind.ControlledDocumentV2 => Controlled(database, limits, clock, principal, request.Envelope,
                GrainNativePayload.Read<ControlledDocumentReadRequestV2>(request.Payload), token),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
    }

    private static OwnedDocumentReadResultV2 Owned(DatabaseEngine database, IOptions<DatabaseLimits> limits,
        TimeProvider clock, PrincipalRecord principal, GrainRequestEnvelope envelope,
        OwnedDocumentReadRequestV2 request, CancellationToken token)
    {
        if (request.Frame is null || request.Frame.ExpiresAt != envelope.ExpiresAt)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        var work = Work(limits, clock, request.MaximumReadBytes, request.MaximumExaminedRecords, request.MaximumResultBytes, token);
        var grant = work.CreateReadGrant(request.MaximumReadBytes, request.MaximumExaminedRecords);
        var result = new OwnedDocumentReadResultV2(database.ReadOwnedDocumentV2(principal.Id, request, work, grant),
            grant.ReadBytes, grant.ExaminedRecords);
        work.CheckResult(result);
        return result;
    }

    private static ControlledDocumentReadResultV2 Controlled(DatabaseEngine database, IOptions<DatabaseLimits> limits,
        TimeProvider clock, PrincipalRecord principal, GrainRequestEnvelope envelope,
        ControlledDocumentReadRequestV2 request, CancellationToken token)
    {
        GrainRequestAuthority.RequireAdministrator(principal);
        if (request.Frame is null || request.Frame.Original is null || request.Frame.Original.ExpiresAt != envelope.ExpiresAt)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        var work = Work(limits, clock, request.MaximumReadBytes, request.MaximumExaminedRecords, request.MaximumResultBytes, token);
        var grant = work.CreateReadGrant(request.MaximumReadBytes, request.MaximumExaminedRecords);
        var result = new ControlledDocumentReadResultV2(database.ReadControlledDocumentV2(principal.Id, request.Frame, work, grant, token),
            grant.ReadBytes, grant.ExaminedRecords);
        work.CheckResult(result);
        return result;
    }

    private static ReadExecutionBudget Work(IOptions<DatabaseLimits> limits, TimeProvider clock,
        long bytes, int records, int resultBytes, CancellationToken token)
    {
        if (bytes <= EmptyBytes || bytes > limits.Value.MaxQueryReadBytes || records <= Empty
            || records > limits.Value.MaxScanRecords || resultBytes <= Empty || resultBytes > limits.Value.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        var work = new ReadExecutionBudget(limits, clock, token);
        work.ConstrainResultBytes(resultBytes);
        return work;
    }
}
