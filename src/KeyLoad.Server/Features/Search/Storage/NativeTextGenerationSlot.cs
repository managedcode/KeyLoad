namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextGenerationSlot
{
    internal NativeTextGenerationSlot(NativeTextGeneration? generation) => Generation = generation;

    internal NativeTextResourceReservation? SharedReservation { get; set; }
    internal NativeTextGeneration? Generation { get; set; }
    internal string? Leaf { get; set; }
    internal bool PhysicalOwnerCreated { get; set; }
    internal bool LeaseActive { get; set; }
    internal bool Unsettled { get; set; }
    internal bool CleanupActive { get; set; }
}
