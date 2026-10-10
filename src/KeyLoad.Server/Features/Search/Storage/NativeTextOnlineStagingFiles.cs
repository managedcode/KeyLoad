using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextOnlineStagingFiles
{
    internal const string FileName = "staging.bin";

    internal static void Write(string path, NativeTextOnlineSession session,
        IOptions<NativeTextExecutionOptions> options, NativeTextResourceOwnership resources)
    {
        var expected = Expected(session);
        session.Budget.ChargeBytes(NativeSerialization.Measure(expected));
        NativeTextFileIO.WriteEnvelope(Path.Combine(path, FileName), expected,
            options.Value.MaximumOwnerReceiptBytes, options, resources);
        Require(path, session, options);
    }

    internal static void Require(string path, NativeTextOnlineSession session,
        IOptions<NativeTextExecutionOptions> options)
    {
        var receipt = Path.Combine(path, FileName);
        NativeTextFileIO.VerifyRegularFile(receipt);
        session.Budget.ChargeBytes(new FileInfo(receipt).Length);
        var actual = NativeTextFileIO.ReadEnvelope<NativeTextOnlineStaging>(receipt,
            options.Value.MaximumOwnerReceiptBytes);
        var expected = Expected(session);
        session.Budget.ChargeBytes(NativeSerialization.Measure(actual));
        session.Budget.ChargeBytes(NativeSerialization.Measure(expected));
        if (!NativeSerialization.Serialize(actual).AsSpan().SequenceEqual(NativeSerialization.Serialize(expected)))
        { throw NativeTextErrors.Mismatch(); }
        session.Budget.Check();
    }

    internal static void RequireCommitted(string path, OnlineTextCurrentPublication current,
        OnlineTextIndexMaintenanceResult result, ReadExecutionBudget budget,
        IOptions<NativeTextExecutionOptions> options)
    {
        var file = Path.Combine(path, FileName);
        NativeTextFileIO.VerifyRegularFile(file);
        budget.ChargeBytes(new FileInfo(file).Length);
        var staged = NativeTextFileIO.ReadEnvelope<NativeTextOnlineStaging>(file, options.Value.MaximumOwnerReceiptBytes);
        var request = new OnlineTextIndexMaintenanceRequest(current.CommandId, current.Consumer,
            current.Authority.Collection, current.Authority.Field, current.ConsumerGeneration,
            result.BaseCut.NodeId, current.Authority.Placement);
        var expected = new NativeTextOnlineStaging(NativeTextProtocol.FormatVersion, result.BaseCut.NodeId,
            current.Authority.Leaf, request, current.PrincipalId, current.ParentFingerprint,
            current.Authority.ExpectedCurrentCommandId, result.BaseCut, current.Authority.DataEpoch);
        budget.ChargeBytes(NativeSerialization.Measure(staged));
        budget.ChargeBytes(NativeSerialization.Measure(expected));
        if (!NativeSerialization.Serialize(staged).AsSpan().SequenceEqual(NativeSerialization.Serialize(expected)))
        { throw NativeTextErrors.Mismatch(); }
        budget.Check();
    }

    private static NativeTextOnlineStaging Expected(NativeTextOnlineSession session)
    {
        var captured = session.Base ?? throw NativeTextErrors.Ownership();
        var leaf = session.Leaf ?? throw NativeTextErrors.Ownership();
        session.Budget.ChargeBytes(NativeSerialization.Measure(session.Request));
        session.Budget.ChargeBytes(SHA256.HashSizeInBytes);
        var fingerprint = OnlineTextParentIdentity.Fingerprint(session.PrincipalId, session.Request);
        return new(NativeTextProtocol.FormatVersion, session.Request.NodeId, leaf, session.Request,
            session.PrincipalId, fingerprint, session.ExpectedCurrentCommandId,
            NativeTextOnlineCapabilityResult.Cut(session.Request.NodeId, captured), captured.DataEpoch);
    }
}
