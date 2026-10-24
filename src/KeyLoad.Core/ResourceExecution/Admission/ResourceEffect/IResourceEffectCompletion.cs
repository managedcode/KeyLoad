namespace KeyLoad.Core.ResourceExecution;

/// <summary>Joins actual successful composition observations to the originally signed lifetime set.</summary>
internal interface IResourceEffectCompletion
{
    void RequireCompleteResourceEffects();
}
