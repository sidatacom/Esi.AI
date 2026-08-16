using System.Text.Json;

namespace Esi.AI.Llm.Redis;

/// <summary>
/// Static extension methods for System.Text.Json serialization/deserialization.
/// </summary>
public static class JsonExtensions
{
    /// <summary>
    /// Serializes an object to a JSON string using SnakeCase naming policy.
    /// </summary>
    public static string ToJson(this object obj)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
        return JsonSerializer.Serialize(obj, options);
    }

    /// <summary>
    /// Deserializes a JSON string to the specified type using SnakeCase naming policy.
    /// </summary>
    public static T FromJson<T>(this string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
        return JsonSerializer.Deserialize<T>(json, options)!;
    }
}