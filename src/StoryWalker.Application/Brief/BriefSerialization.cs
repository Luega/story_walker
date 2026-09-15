using System.Text.Json;

namespace StoryWalker.Application.Brief;

/// <summary>
/// Single source of truth for how the <see cref="GenerationBrief"/> is (de)serialized on the wire,
/// shared by the producer (<c>apps/api</c>) and the consumer (<c>apps/functions</c>) so the queue
/// message never drifts. The contract is camelCase JSON (ADR-0013).
/// </summary>
public static class BriefSerialization
{
    /// <summary>camelCase, web-defaults options used for the brief queue message body.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(GenerationBrief brief) =>
        JsonSerializer.Serialize(brief, Options);

    public static GenerationBrief Deserialize(string json) =>
        JsonSerializer.Deserialize<GenerationBrief>(json, Options)
        ?? throw new JsonException("The brief message body deserialized to null.");
}
