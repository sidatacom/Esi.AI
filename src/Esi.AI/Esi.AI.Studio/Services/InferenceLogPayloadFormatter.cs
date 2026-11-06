using System.Text.Json;
using Esi.AI.Models;

namespace Esi.AI.Studio.Services;

internal static class InferenceLogPayloadFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string FormatOpenAiRequest(OpenAiBackendChatRequest request, bool streaming) =>
        JsonSerializer.Serialize(new
        {
            model = request.Model,
            stream = streaming,
            messages = request.Messages.Select(FormatMessage),
            tools = request.Tools,
            options = request.Options
        }, JsonOptions);

    public static string FormatStudioRequest(PersistedChat chat, ChatExchangeRequest request, bool streaming)
    {
        var messages = chat.Messages
            .Select(message => (object)new { role = message.Role, content = message.Content })
            .Append(new
            {
                role = "user",
                content = request.Content,
                contentParts = request.ContentParts,
                images = request.Images?.Select(FormatImage)
            })
            .ToArray();

        return JsonSerializer.Serialize(new
        {
            model = request.ModelPath,
            backend = request.Backend,
            backendVariantId = request.BackendVariantId,
            stream = streaming,
            messages
        }, JsonOptions);
    }

    public static string FormatResponse(GenerationResult result) =>
        JsonSerializer.Serialize(new
        {
            text = result.Text,
            toolCalls = result.ToolCalls,
            finishReason = result.FinishReason,
            tokenCount = result.TokenCount,
            tokensPerSecond = result.TokensPerSecond
        }, JsonOptions);

    private static object FormatMessage(ChatMessage message) =>
        new
        {
            role = message.Role,
            content = message.Content,
            contentParts = message.ContentParts,
            images = message.Images?.Select(FormatImage),
            toolCalls = message.ToolCalls,
            toolCallId = message.ToolCallId
        };

    private static object FormatImage(ChatImage image) =>
        new
        {
            mediaType = image.MediaType,
            sizeBytes = image.Data.Length
        };
}
