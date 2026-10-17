using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class EventVectorFirstLookupRows
{
    internal static ReadOnlyMemory<byte> Read(IKeyValueView boundedView, byte[] actualKey,
        EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(boundedView);
        ArgumentNullException.ThrowIfNull(actualKey);
        ArgumentNullException.ThrowIfNull(admission);
        var original = ReadOnlyMemory<byte>.Empty;
        boundedView.ReadValue(actualKey, bytes =>
        {
            admission.RequireEncodedBytes(checked(actualKey.LongLength + bytes.Length));
            original = bytes.ToArray();
        });
        return original;
    }

    internal static ReadOnlyMemory<byte> Active(IKeyValueView view, PrincipalRecord principal,
        EventFeedControlRequest request, EventVectorAdmissionPolicy admission)
        => Read(view, EventVectorKeys.FirstIssuance(request.ControlPartition,
            principal.Id, request.CommandId), admission);

    internal static ReadOnlyMemory<byte> Terminal(IKeyValueView view, PrincipalRecord principal,
        EventFeedControlRequest request, EventVectorAdmissionPolicy admission)
        => Read(view, EventVectorKeys.TerminalFirstIssuance(request.ControlPartition,
            principal.Id, request.CommandId), admission);

    internal static ReadOnlyMemory<byte> Parent(IKeyValueView view,
        EventFeedControlRequest request, EventVectorAdmissionPolicy admission)
        => Read(view, EventVectorKeys.ParentCall(request.ControlPartition, request.MapId), admission);

    internal static ReadOnlyMemory<byte> Index(IKeyValueView view, PrincipalRecord principal,
        EventFeedControlRequest request, EventVectorAdmissionPolicy admission)
        => Read(view, EventVectorKeys.ParentOutcome(request.ControlPartition,
            principal.Id, request.CommandId), admission);
}

