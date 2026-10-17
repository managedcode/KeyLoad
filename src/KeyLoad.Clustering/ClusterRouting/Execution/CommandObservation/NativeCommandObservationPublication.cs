using KeyLoad.Core;

namespace KeyLoad.Clustering;

/// <summary>Retains both allocated reply bodies until the original native producer has settled.</summary>
internal sealed class NativeCommandObservationPublication<T>(
    Func<GrainOperationReply, ReadExecutionBudget, T> publish, Action<T> clearOnFailure)
{
    private GrainOperationReply? inner;
    private T? value;
    private bool captured;

    internal T Value => captured ? value!
        : throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest);

    internal T Capture(GrainOperationReply reply, ReadExecutionBudget work)
    {
        if (inner is not null)
        {
            throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest);
        }
        inner = reply;
        value = publish(reply, work);
        captured = true;
        return value;
    }

    internal Exception Clear(Exception original)
    {
        if (captured)
        {
            try
            {
                clearOnFailure(value!);
            }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                original = NativeCommandObservationCompletion.Retain(original, error)!;
            }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                original = NativeCommandObservationCompletion.Retain(original, error)!;
            }
        }
        try
        {
            NativeCommandObservationEncoding.Clear(inner);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            original = NativeCommandObservationCompletion.Retain(original, error)!;
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            original = NativeCommandObservationCompletion.Retain(original, error)!;
        }
        return original;
    }
}
