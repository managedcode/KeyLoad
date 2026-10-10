using System.Security.Cryptography;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class NativeSignedPurposeObserver(NativeDiscoveryOmissionFiles files,
    IHttpContextAccessor accessor, string localVoter, IOptions<RequestProbeExecutionOptions> options)
    : IGrainRequestPurposeObserver
{
    private readonly NativeSignedPurposeJson json = new(options);
    internal void RequireEnrollment() { _ = Arm(); files.RequirePurposeReceiver(localVoter); }
    public string? BeforeSign(GrainRequestEnvelope original)
    {
        var context = accessor.HttpContext;
        if (context is null || original.CommandKind != OperationKind.Batch
            || context.Items[ServerProtocol.PrincipalItem] is not PrincipalRecord principal
            || principal.Id != original.PrincipalId || context.Features.Get<ServerConnectionFeature>() is not { } connection)
        { return null; }
        return files.Locked(() => BeforeSignLocked(original, context, connection));
    }
    private string? BeforeSignLocked(GrainRequestEnvelope original, HttpContext context, ServerConnectionFeature connection)
    {
        var arm = Arm();
        if (arm.SourceVoter != localVoter || arm.CommandId != original.CommandId || !Route(context.Request.Path, arm.Route))
        { return null; }
        context.RequestAborted.ThrowIfCancellationRequested();
        if (GrainNativePayload.Read<CommandRequest>(original.Payload).CommandId != original.CommandId)
        { throw Invalid(); }
        if (files.PurposeRecord(NativeSignedPurposeProtocol.IssuedFile) is not null)
        { throw Invalid(); }
        var issued = new NativeSignedPurposeWitness(arm.Version, arm.SessionId, arm.ArmId, arm.SourceVoter,
            arm.Route, connection.Id, original.RequestId, original.CommandId, original.Incarnation,
            original.PrincipalId!, original.ExpiresAt, original.Purpose, Digest(original), NativeSignedPurposeProtocol.Issued, null);
        files.WritePurpose(NativeSignedPurposeProtocol.IssuedFile, json.Write(issued));
        context.RequestAborted.ThrowIfCancellationRequested();
        return NativeSignedPurposeProtocol.InvalidPurpose;
    }
    public void RejectedAfterMac(GrainRequestEnvelope actual, KeyLoadException failure, GrainRequestScopeCut cut)
    {
        files.Locked(() =>
        {
            var arm = Arm();
            var issued = json.Witness(files.PurposeRecord(NativeSignedPurposeProtocol.IssuedFile) ?? throw Invalid());
            if (issued.ArmId != arm.ArmId || issued.SessionId != arm.SessionId || issued.SourceVoter != arm.SourceVoter
                || issued.Route != arm.Route || issued.CommandId != arm.CommandId
                || issued.Stage != NativeSignedPurposeProtocol.Issued || issued.ReceiverVoter is not null
                || actual.Purpose != NativeSignedPurposeProtocol.InvalidPurpose || failure.Code != ErrorCode.TokenInvalidated
                || actual.RequestId != issued.RequestId || actual.CommandId != issued.CommandId
                || actual.Incarnation != issued.Incarnation || actual.PrincipalId != issued.PrincipalId
                || actual.ExpiresAt != issued.ExpiresAt || actual.ReadKind is not null || actual.CommandKind != OperationKind.Batch
                || Digest(actual) != issued.PayloadDigest || files.PurposeRecord(NativeSignedPurposeProtocol.RejectedFile) is not null)
            { throw Invalid(); }
            GrainRequestScope.Validate(actual with { Purpose = issued.OriginalPurpose }, cut.Incarnation, cut.EvaluatedAt, cut.MaximumFuture);
            if (GrainNativePayload.Read<CommandRequest>(actual.Payload).CommandId != issued.CommandId)
            { throw Invalid(); }
            files.WritePurpose(NativeSignedPurposeProtocol.RejectedFile,
                json.Write(issued with { Stage = NativeSignedPurposeProtocol.Rejected, ReceiverVoter = localVoter }));
            return true;
        });
    }
    private NativeSignedPurposeArm Arm()
    {
        var arm = json.Arm(files.PurposeRecord(NativeSignedPurposeProtocol.ArmFile) ?? throw Invalid());
        files.RequirePurposeArm(arm);
        return arm;
    }
    private static bool Route(PathString path, string route) => route switch
    {
        NativeDiscoveryOmissionProtocol.SdkRoute => DocumentApi.IsCommandPath(path),
        NativeDiscoveryOmissionProtocol.McpRoute => path == McpFramingProtocol.Path,
        _ => false
    };
    private static string Digest(GrainRequestEnvelope request) => Convert.ToHexString(SHA256.HashData(request.Payload.Span));
    private static InvalidOperationException Invalid() => new(NativeSignedPurposeProtocol.Invalid);
}
