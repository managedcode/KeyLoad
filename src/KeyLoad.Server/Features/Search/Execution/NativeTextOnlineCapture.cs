using KeyLoad.Core;
using KeyLoad.Core.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextOnlineCapture
{
    internal static void Execute(DatabaseEngine database, NativeTextOnlineSession session,
        NativeTextResourceOwnership resources, TimeProvider clock)
    {
        var budget = session.Budget;
        budget.Check();
        if (session.SourcePin is not null || session.Base is not null)
        { throw NativeTextErrors.Mismatch(); }
        var request = session.Request;
        session.OperationReservation = resources.ReserveLease(budget);
        session.ExpectedCurrentCommandId = database.Store.Read(raw =>
        {
            var view = budget.CreateView(raw);
            var principal = database.Principal(view, session.PrincipalId, clock.GetUtcNow());
            return database.ReadOnlineTextPublicationHead(view, principal, request, budget);
        });
        session.SourcePin = NativeTextOnlineSourcePin.Capture(database, session.PrincipalId,
            new(request.Consumer, request.ConsumerGeneration, request.Collection, request.Field,
                request.NodeId, request.Placement), budget);
        var captured = session.SourcePin.CapturedMetadata;
        session.Base = captured;
        session.Current = captured;
        session.Scope = new(request.NodeId, captured.Incarnation, captured.DataEpoch, captured.ReadGeneration,
            captured.Position, request.Consumer.Partition, request.Collection, request.Field,
            captured.PrincipalId, captured.PolicyEpoch, captured.SchemaVersion);
    }

    internal static NativeTextSeedCapture CurrentMetadata(DatabaseEngine database,
        NativeTextOnlineSession session)
        => database.Store.Read(raw => NativeTextSeedCollector.CaptureMetadataView(database, raw,
            session.PrincipalId, new(session.Request.Consumer, session.Request.ConsumerGeneration,
                session.Request.Collection, session.Request.Field, session.Request.NodeId,
                session.Request.Placement), session.Budget));

    internal static void RequireSameAuthority(NativeTextOnlineSession session, NativeTextSeedCapture fresh)
    {
        var original = session.Base ?? throw NativeTextErrors.Ownership();
        if (fresh.PrincipalId != original.PrincipalId || fresh.PolicyEpoch != original.PolicyEpoch
            || fresh.Incarnation != original.Incarnation || fresh.DataEpoch != original.DataEpoch
            || fresh.ReadGeneration != original.ReadGeneration || fresh.SchemaVersion != original.SchemaVersion
            || fresh.ResourceSha256 != original.ResourceSha256 || fresh.Position < original.Position
            || fresh.AppliedPosition < original.AppliedPosition || fresh.UpperSequence < original.UpperSequence)
        { throw NativeTextErrors.Mismatch(); }
    }
}
