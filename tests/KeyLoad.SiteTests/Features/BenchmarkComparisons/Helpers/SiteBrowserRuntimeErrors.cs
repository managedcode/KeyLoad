using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteBrowserRuntimeErrors
{
    public static IReadOnlyList<SiteBrowserError> Read(SiteBrowserCdpClient cdp)
    {
        List<SiteBrowserError> errors = [];
        foreach (var item in cdp.DrainEvents())
        {
            AddEventError(item, errors);
        }

        return errors;
    }

    private static void AddEventError(JsonElement item, List<SiteBrowserError> errors)
    {
        if (!item.TryGetProperty(SiteBrowserTokens.MethodField, out var method))
        {
            return;
        }

        switch (method.GetString())
        {
            case SiteBrowserUiTokens.RuntimeErrorEvent:
                errors.Add(ReadException(item));
                break;
            case SiteBrowserUiTokens.RuntimeConsoleEvent:
                AddRuntimeConsoleError(item, errors);
                break;
            case SiteBrowserUiTokens.ConsoleEvent:
                AddConsoleError(item, errors);
                break;
        }
    }

    private static void AddRuntimeConsoleError(JsonElement item, List<SiteBrowserError> errors)
    {
        var parameters = item.GetProperty(SiteBrowserTokens.ParamsField);
        var type = parameters.GetProperty(SiteBrowserUiTokens.ConsoleTypeField).GetString();
        if (type is not (SiteBrowserUiTokens.ConsoleErrorType or SiteBrowserUiTokens.ConsoleAssertType))
        {
            return;
        }

        var message = string.Join(SiteBrowserUiTokens.ConsoleMessageSeparator,
            parameters.GetProperty(SiteBrowserUiTokens.ConsoleArgumentsField).EnumerateArray()
                .Select(ReadConsoleArgument));
        errors.Add(new(SiteBrowserUiTokens.RuntimeConsoleEvent, message));
    }

    private static string ReadConsoleArgument(JsonElement argument)
    {
        if (argument.TryGetProperty(SiteBrowserUiTokens.ConsoleValueField, out var value))
        {
            return value.ToString();
        }

        if (argument.TryGetProperty(SiteBrowserUiTokens.ConsoleDescriptionField, out var description))
        {
            return description.GetString() ?? SiteBrowserTokens.ErrorStatus;
        }

        return SiteBrowserTokens.ErrorStatus;
    }

    private static SiteBrowserError ReadException(JsonElement item)
    {
        var details = item.GetProperty(SiteBrowserTokens.ParamsField).GetProperty(SiteBrowserTokens.ExceptionDetailsField);
        var text = details.TryGetProperty(SiteBrowserTokens.ExceptionField, out var exception)
            && exception.TryGetProperty(SiteBrowserTokens.DescriptionField, out var description)
            ? description.GetString() : SiteBrowserTokens.ErrorStatus;
        return new(SiteBrowserUiTokens.RuntimeErrorEvent, text ?? SiteBrowserTokens.ErrorStatus);
    }

    private static void AddConsoleError(JsonElement item, List<SiteBrowserError> errors)
    {
        var entry = item.GetProperty(SiteBrowserTokens.ParamsField).GetProperty(SiteBrowserUiTokens.ConsoleEntryField);
        if (entry.GetProperty(SiteBrowserUiTokens.ConsoleLevelField).GetString() != SiteBrowserUiTokens.ErrorLevel)
        {
            return;
        }
        var text = entry.GetProperty(SiteBrowserTokens.ErrorMessageText).GetString() ?? SiteBrowserTokens.ErrorStatus;
        errors.Add(new(SiteBrowserUiTokens.ConsoleEvent, text));
    }
}
