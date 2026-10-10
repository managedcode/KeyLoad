namespace KeyLoad.Core;

internal enum EventVectorMapState
{
    Opening = 0,
    Active = 1,
    Acknowledging = 2,
    Refreshing = 3,
    Releasing = 4,
    Released = 5,
}
