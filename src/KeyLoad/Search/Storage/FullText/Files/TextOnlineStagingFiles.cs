using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

internal static class TextOnlineStagingFiles
{
    internal const string FileName = "staging.bin";

    internal static void Write(string path, TextOnlineSession session,
        IOptions<TextExecutionOptions> options, TextResourceOwnership resources)
    {
        var expected = Expected(session);
        session.Budget.ChargeBytes(OrleansSerialization.Measure(expected));
        TextFileIO.WriteEnvelope(Path.Combine(path, FileName), expected,
            options.Value.MaximumOwnerReceiptBytes, options, resources);
        Require(path, session, options);
    }

    internal static void Require(string path, TextOnlineSession session,
        IOptions<TextExecutionOptions> options)
    {
        var receipt = Path.Combine(path, FileName);
        TextFileIO.VerifyRegularFile(receipt);
        session.Budget.ChargeBytes(new FileInfo(receipt).Length);
        var actual = TextFileIO.ReadEnvelope<TextOnlineStaging>(receipt,
            options.Value.MaximumOwnerReceiptBytes);
        var expected = Expected(session);
        session.Budget.ChargeBytes(OrleansSerialization.Measure(actual));
        session.Budget.ChargeBytes(OrleansSerialization.Measure(expected));
        if (!OrleansSerialization.Serialize(actual).AsSpan().SequenceEqual(OrleansSerialization.Serialize(expected)))
        {
            throw TextErrors.Mismatch();
        }
        session.Budget.Check();
    }

    internal static void RequireCommitted(string path, OnlineTextCurrentPublication current,
        OnlineTextIndexMaintenanceResult result, ReadExecutionBudget budget,
        IOptions<TextExecutionOptions> options)
    {
        var file = Path.Combine(path, FileName);
        TextFileIO.VerifyRegularFile(file);
        budget.ChargeBytes(new FileInfo(file).Length);
        var staged = TextFileIO.ReadEnvelope<TextOnlineStaging>(file, options.Value.MaximumOwnerReceiptBytes);
        var request = new OnlineTextIndexMaintenanceRequest(current.CommandId, current.Consumer,
            current.Authority.Collection, current.Authority.Field, current.ConsumerGeneration,
            result.BaseCut.NodeId, current.Authority.Placement);
        var expected = new TextOnlineStaging(TextProtocol.FormatVersion, result.BaseCut.NodeId,
            current.Authority.Leaf, request, current.PrincipalId, current.ParentFingerprint,
            current.Authority.ExpectedCurrentCommandId, result.BaseCut, current.Authority.DataEpoch);
        budget.ChargeBytes(OrleansSerialization.Measure(staged));
        budget.ChargeBytes(OrleansSerialization.Measure(expected));
        if (!OrleansSerialization.Serialize(staged).AsSpan().SequenceEqual(OrleansSerialization.Serialize(expected)))
        {
            throw TextErrors.Mismatch();
        }
        budget.Check();
    }

    private static TextOnlineStaging Expected(TextOnlineSession session)
    {
        var captured = session.Base ?? throw TextErrors.Ownership();
        var leaf = session.Leaf ?? throw TextErrors.Ownership();
        session.Budget.ChargeBytes(OrleansSerialization.Measure(session.Request));
        session.Budget.ChargeBytes(SHA256.HashSizeInBytes);
        var fingerprint = OnlineTextParentIdentity.Fingerprint(session.PrincipalId, session.Request);
        return new(TextProtocol.FormatVersion, session.Request.NodeId, leaf, session.Request,
            session.PrincipalId, fingerprint, session.ExpectedCurrentCommandId,
            TextOnlineCapabilityResult.Cut(session.Request.NodeId, captured), captured.DataEpoch);
    }
}
