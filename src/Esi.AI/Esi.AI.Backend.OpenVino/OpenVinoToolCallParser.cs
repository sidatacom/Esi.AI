using System.Text.Json;
using System.Text.RegularExpressions;
using Esi.AI.Models;

namespace Esi.AI.Backend.OpenVino;

internal static partial class OpenVinoToolCallParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static OpenVinoToolCallParseResult Parse(string text)
    {
        var toolCalls = new List<OpenAiToolCall>();
        var visibleText = new System.Text.StringBuilder(text.Length);
        var position = 0;
        foreach (Match block in ToolCallBlockRegex().Matches(text))
        {
            visibleText.Append(text, position, block.Index - position);
            var parsedCalls = ParseBlock(block.Groups["body"].Value);
            if (parsedCalls.Count == 0)
                visibleText.Append(block.Value);
            else
                toolCalls.AddRange(parsedCalls);
            position = block.Index + block.Length;
        }

        visibleText.Append(text, position, text.Length - position);
        if (toolCalls.Count == 0 && TryParseJsonToolCalls(text, out var jsonToolCalls))
            return new OpenVinoToolCallParseResult(string.Empty, jsonToolCalls);

        return new OpenVinoToolCallParseResult(visibleText.ToString().Trim(), toolCalls);
    }

    private static IReadOnlyList<OpenAiToolCall> ParseBlock(string body)
    {
        var functionMatch = FunctionCallRegex().Match(body);
        if (functionMatch.Success)
        {
            var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (Match parameter in ParameterRegex().Matches(functionMatch.Groups["body"].Value))
            {
                var value = parameter.Groups["value"].Value.Trim();
                arguments[parameter.Groups["name"].Value] = ParseParameter(value);
            }

            return [new OpenAiToolCall(
                $"call_{Guid.NewGuid():N}",
                "function",
                new OpenAiToolCallFunction(
                    functionMatch.Groups["name"].Value,
                    JsonSerializer.Serialize(arguments, JsonOptions)))];
        }

        return TryParseJsonToolCalls(body, out var jsonToolCalls) ? jsonToolCalls : [];
    }

    private static bool TryParseJsonToolCalls(string text, out IReadOnlyList<OpenAiToolCall> toolCalls)
    {
        toolCalls = [];
        try
        {
            using var document = JsonDocument.Parse(text.Trim());
            var root = document.RootElement;
            IEnumerable<JsonElement> elements = root.ValueKind == JsonValueKind.Array
                ? root.EnumerateArray()
                : [root];
            var parsedToolCalls = new List<OpenAiToolCall>();
            foreach (var element in elements)
            {
                if (element.ValueKind != JsonValueKind.Object ||
                    !element.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String ||
                    !element.TryGetProperty("arguments", out var arguments))
                    return false;

                var argumentsText = arguments.ValueKind == JsonValueKind.String
                    ? arguments.GetString() ?? "{}"
                    : arguments.GetRawText();
                parsedToolCalls.Add(new OpenAiToolCall(
                    $"call_{Guid.NewGuid():N}",
                    "function",
                    new OpenAiToolCallFunction(name.GetString()!, argumentsText)));
            }

            if (parsedToolCalls.Count == 0)
                return false;
            toolCalls = parsedToolCalls;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static JsonElement ParseParameter(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(value, JsonOptions));
            return document.RootElement.Clone();
        }
    }

    [GeneratedRegex(@"<tool_call>(?<body>.*?)</tool_call>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ToolCallBlockRegex();

    [GeneratedRegex(@"<function=(?<name>[^>\s]+)>(?<body>.*?)</function>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex FunctionCallRegex();

    [GeneratedRegex(@"<parameter=(?<name>[^>\s]+)>(?<value>.*?)</parameter>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ParameterRegex();
}

internal sealed record OpenVinoToolCallParseResult(string Text, IReadOnlyList<OpenAiToolCall> ToolCalls);