using System.Text.Json;
using System.Text.Json.Serialization;

namespace Penghou.Baize.Gemini;

/// <summary>
/// Wire model for a function response returned by the application after the
/// model requested a function call. Sent on a user-role message so the model
/// can continue.
/// </summary>
internal sealed class GeminiFunctionResponse
{
    /// <summary>
    /// The function-call id this response answers. Gemini requires the exact
    /// id from the preceding function call to be echoed back during
    /// function-calling conversations.
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>The name of the function that was executed.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The function's result as a JSON value.</summary>
    [JsonPropertyName("response")]
    public required JsonElement Response { get; init; }
}
