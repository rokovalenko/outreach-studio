using System.Text.Json;
using System.Text.Json.Serialization;

namespace OutreachStudio.Web.Client.Api;

/// <summary>
/// The JSON shape both hosts agree on: web defaults plus enum names, because the server writes
/// enums as names. The rule tree is not serialised with these options, it goes through
/// <see cref="OutreachStudio.Engine.Rules.Rule.ToJson"/> so the stored version is byte for byte
/// what the engine reads back.
/// </summary>
public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
