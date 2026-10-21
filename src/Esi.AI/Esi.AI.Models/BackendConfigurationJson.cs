using System.Text.Json;
using System.Text.Json.Serialization;

namespace Esi.AI.Models;

/// <summary>Reads and writes the single active, variant-specific block in a model profile.</summary>
public static class BackendConfigurationJson
{
    private static readonly JsonSerializerOptions DefaultOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Serializes a typed payload under its concrete backend variant identifier.</summary>
    public static string Serialize<TPayload>(
        string backendVariantId,
        TPayload payload,
        JsonSerializerOptions? options = null)
        where TPayload : class
    {
        ArgumentNullException.ThrowIfNull(payload);
        var variantId = NormalizeVariantId(backendVariantId);
        return JsonSerializer.Serialize(new Dictionary<string, TPayload>
        {
            [variantId] = payload
        }, options ?? DefaultOptions);
    }

    /// <summary>Deserializes the selected variant block into its typed request.</summary>
    public static TPayload Deserialize<TPayload>(
        string configurationJson,
        string backendVariantId,
        JsonSerializerOptions? options = null)
        where TPayload : class
    {
        var payload = GetPayloadElement(configurationJson, backendVariantId);
        return payload.Deserialize<TPayload>(options ?? DefaultOptions)
            ?? throw new JsonException("The backend configuration payload is empty.");
    }

    /// <summary>Returns the active payload from a single-block profile.</summary>
    public static JsonElement GetPayloadElement(
        string configurationJson,
        string? expectedVariantId = null) =>
        GetPayloadElement(configurationJson, expectedVariantId, out _);

    /// <summary>Returns the active payload and the block's variant identifier, if wrapped.</summary>
    public static JsonElement GetPayloadElement(
        string configurationJson,
        string? expectedVariantId,
        out string? actualVariantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationJson);
        using var document = JsonDocument.Parse(configurationJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("A backend configuration must be a JSON object.");

        var properties = root.EnumerateObject().ToArray();
        var variantProperties = properties.Where(property => IsVariantId(property.Name)).ToArray();
        if (variantProperties.Length == 0)
        {
            actualVariantId = null;
            throw new JsonException("A backend configuration must contain a variant block.");
        }

        if (properties.Length != 1 || variantProperties.Length != 1)
            throw new JsonException("A model profile must contain exactly one backend variant block.");

        var variantProperty = variantProperties[0];
        actualVariantId = NormalizeVariantId(variantProperty.Name);
        if (!string.IsNullOrWhiteSpace(expectedVariantId) &&
            !string.Equals(actualVariantId, NormalizeVariantId(expectedVariantId), StringComparison.Ordinal))
            throw new JsonException($"The profile contains '{actualVariantId}', not the selected backend variant '{expectedVariantId}'.");

        if (variantProperty.Value.ValueKind != JsonValueKind.Object)
            throw new JsonException("A backend variant block must contain a JSON object.");

        return variantProperty.Value.Clone();
    }

    /// <summary>Gets the variant identifier from a single-block profile.</summary>
    public static string GetBackendVariantId(string configurationJson)
    {
        _ = GetPayloadElement(configurationJson, null, out var variantId);
        return variantId!;
    }

    private static string NormalizeVariantId(string backendVariantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backendVariantId);
        var normalized = backendVariantId.Trim().ToLowerInvariant();
        if (!IsVariantId(normalized))
            throw new ArgumentException("A backend variant identifier is required.", nameof(backendVariantId));

        return normalized;
    }

    private static bool IsVariantId(string value)
    {
        if (value.Equals("openvino", StringComparison.OrdinalIgnoreCase))
            return true;

        var segments = value.Split('.');
        return segments.Length >= 2 && segments.All(segment =>
            segment.Length > 0 && segment.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'));
    }
}