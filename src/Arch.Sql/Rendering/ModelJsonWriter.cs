using System.Text.Json;
using Arch.Sql.Model;

namespace Arch.Sql.Rendering;

public static class ModelJsonWriter
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void Write(SqlModel model, string path)
    {
        using var stream = new FileStream(path, FileMode.Create);
        JsonSerializer.Serialize(stream, model, Options);
    }
}
