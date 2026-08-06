using System.Text.Json;

namespace Arch.Core.Serialization;

/// <summary>The JSON contract every model.json is written and read with. Character-identical
/// in both products before the core was extracted.</summary>
public static class ModelJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
