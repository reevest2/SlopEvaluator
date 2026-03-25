using System.Text.Json;
using System.Text.Json.Serialization;

namespace SlopEvaluator.Shared.Json;

/// <summary>Shared JSON serialization options.</summary>
public static class JsonDefaults
{
    public static JsonSerializerOptions Create() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };
}
