namespace KeyLoad.Server.Features.Search;

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
    internal bool LeaseActive
    {
        get; set;
    }
    internal bool Unsettled
    {
        get; set;
    }
    internal bool CleanupActive
    {
        get; set;
    }
}
