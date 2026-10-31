namespace KeyLoad.Server.Search;

internal sealed class TextGenerationSlot
{
    internal TextGenerationSlot(TextGeneration? generation) => Generation = generation;

    internal TextResourceReservation? SharedReservation
    {
        get; set;
    }
    internal TextGeneration? Generation
    {
        get; set;
    }
    internal string? Leaf
    {
        get; set;
    }
    internal bool PhysicalOwnerCreated
    {
        get; set;
    }
    internal int Readers { get; set; }
    internal int SettlingReaders { get; set; }
    internal bool LeaseActive => Readers > 0 || SettlingReaders > 0;
    internal bool Invalidated { get; set; }
    internal bool Unsettled
    {
        get; set;
    }
    internal bool CleanupActive
    {
        get; set;
    }
}
