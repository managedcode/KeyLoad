namespace KeyLoad.UnitTests.Features.Messaging;

/// <summary>Advances only after the real owner's committed position crosses the armed cut.</summary>
internal sealed class MultiLaneReceiveExpiryClock : TimeProvider
{
    private readonly DateTimeOffset initial = TimeProvider.System.GetUtcNow();
    private Func<long>? position;
    private long armed;
    internal long ObservedPosition { get; private set; }
    private bool advanced;

    internal void Arm(Func<long> nativePosition, long committedCut)
    {
        position = nativePosition;
        armed = committedCut;
    }

    public override DateTimeOffset GetUtcNow()
    {
        if (!advanced && position is { } observe && observe() > armed)
        {
            ObservedPosition = observe();
            advanced = true;
        }
        return advanced ? initial.AddMinutes(2) : initial;
    }
}
