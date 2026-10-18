using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using ManagedCode.Communication.Orleans.Converters;
using ManagedCode.Communication.Orleans.Surrogates;

namespace KeyLoad.Features.InternalSerialization;

// Inspect the selected library's actual generated surrogates; do not mirror their wire contracts.
internal static class NativeCommunicationContracts
{
    internal static Type? Surrogate(Type type)
    {
        if (type == typeof(Command<DatabaseCommand>))
        {
            return typeof(CommandTSurrogate<DatabaseCommand>);
        }
        if (type == typeof(Result<object>))
        {
            return typeof(ResultTSurrogate<object>);
        }
        if (type == typeof(Problem))
        {
            return typeof(ProblemSurrogate);
        }
        return type == typeof(CommandMetadata) ? typeof(CommandMetadataSurrogate) : null;
    }

    internal static bool IsSurrogate(Type type)
        => type == typeof(CommandTSurrogate<DatabaseCommand>) || type == typeof(ResultTSurrogate<object>)
            || type == typeof(ProblemSurrogate) || type == typeof(CommandMetadataSurrogate);

    internal static bool TryProject(object value, out object? surrogate)
    {
        surrogate = value switch
        {
            Command<DatabaseCommand> command => new CommandTSurrogateConverter<DatabaseCommand>().ConvertToSurrogate(command),
            Result<object> result => ProjectResult(result),
            Problem problem => new ProblemSurrogateConverter().ConvertToSurrogate(problem),
            CommandMetadata metadata => new CommandMetadataSurrogateConverter().ConvertToSurrogate(metadata),
            _ => null
        };
        return surrogate is not null;
    }

    private static ResultTSurrogate<object> ProjectResult(Result<object> result)
    {
        if (result.IsSuccess ? result.Value is null || result.Problem is not null
            : result.Value is not null || result.Problem is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, NativePayloadVersion.InvalidPayload);
        }
        if (result.IsFailed)
        {
            _ = Errors.Code(result.Problem);
        }
        return new ResultTSurrogateConverter<object>().ConvertToSurrogate(result);
    }
}
