using Penghou.Nuwa;
using Penghou.Baize;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Penghou.Baize.Tools;

/// <summary>
/// Base implementation of <see cref="ILlmToolResultParser{TResult}"/> that
/// locates the tool call matching <paramref name="toolName"/>, validates its
/// JSON against <paramref name="expectation"/> (including duplicate-property
/// detection), and deserializes it to <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="TResult">The deserialized result type.</typeparam>
/// <param name="toolName">The name of the tool this parser handles.</param>
/// <param name="expectation">The JSON Schema expectation for the tool arguments.</param>
public abstract class LlmToolResultParserBase<TResult>(
    string toolName,
    JsonSchemaExpectation expectation) : ILlmToolResultParser<TResult>
{
    /// <summary>
    /// Gets the name of the tool this parser handles.
    /// </summary>
    protected string ToolName { get; } = toolName;

    /// <inheritdoc />
    public ToolCallParseResult<TResult> Parse(LlmResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var toolCall = response.ToolCalls?
            .FirstOrDefault(tc => string.Equals(tc.Name, ToolName, StringComparison.Ordinal));

        if (toolCall is null)
        {
            return ToolCallParseResult<TResult>.Failed(
                ToolCallParseFailure.MissingToolCall,
                $"No '{ToolName}' tool call found.", response.Content);
        }

        return ParseArguments(toolCall.ArgumentsJson);
    }

    private ToolCallParseResult<TResult> ParseArguments(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
            return ToolCallParseResult<TResult>.Failed(
                ToolCallParseFailure.EmptyArguments,
                $"{ToolName} arguments were empty.",
                argumentsJson);

        JsonDocument argumentsDocument;

        try
        {
            argumentsDocument =
                JsonDocument.Parse(argumentsJson);
        }
        catch (JsonException ex)
        {
            return ToolCallParseResult<TResult>.Failed(
                ToolCallParseFailure.InvalidJson,
                $"Invalid JSON: {ex.Message}",
                argumentsJson);
        }

        using (argumentsDocument)
        {
            if (TryFindDuplicateProperty(
                    argumentsDocument.RootElement,
                    "$",
                    out var duplicatePath))
            {
                return ToolCallParseResult<TResult>.Failed(
                    ToolCallParseFailure.SchemaValidationFailed,
                    $"Schema validation failed: duplicate property '{duplicatePath}'.",
                    argumentsJson);
            }
        }

        JsonNode? argumentsNode;

        try
        {
            argumentsNode =
                JsonNode.Parse(argumentsJson);
        }
        catch (JsonException ex)
        {
            return ToolCallParseResult<TResult>.Failed(
                ToolCallParseFailure.InvalidJson,
                $"Invalid JSON: {ex.Message}",
                argumentsJson);
        }

        if (argumentsNode is null)
        {
            return ToolCallParseResult<TResult>.Failed(
                ToolCallParseFailure.DeserializationFailed,
                $"{ToolName} arguments deserialized to null.",
                argumentsJson);
        }

        IReadOnlyList<string> validationErrors;

        try
        {
            validationErrors =
                expectation.Validate(argumentsNode);
        }
        catch (ArgumentException ex)
        {
            return ToolCallParseResult<TResult>.Failed(
                ToolCallParseFailure.SchemaValidationFailed,
                $"Schema validation could not materialize the JSON object: {ex.Message}",
                argumentsJson);
        }

        if (validationErrors.Count > 0)
        {
            return ToolCallParseResult<TResult>.Failed(
                ToolCallParseFailure.SchemaValidationFailed,
                $"Schema validation failed: {string.Join(" ", validationErrors)}",
                argumentsJson);
        }

        try
        {
            var result = argumentsNode.Deserialize<TResult>();

            return result is null
                ? ToolCallParseResult<TResult>.Failed(
                    ToolCallParseFailure.DeserializationFailed,
                    "Deserialized to null.",
                    argumentsJson)
                : ToolCallParseResult<TResult>.Success(result);
        }
        catch (JsonException ex)
        {
            return ToolCallParseResult<TResult>.Failed(
                ToolCallParseFailure.DeserializationFailed,
                ex.Message,
                argumentsJson);
        }
    }

    private static bool TryFindDuplicateProperty(
        JsonElement element,
        string path,
        out string duplicatePath)
    {
        if (element.ValueKind ==
            JsonValueKind.Object)
        {
            var propertyNames =
                new HashSet<string>(
                    StringComparer.Ordinal);

            foreach (var property
                     in element.EnumerateObject())
            {
                var propertyPath =
                    $"{path}.{property.Name}";

                if (!propertyNames.Add(
                        property.Name))
                {
                    duplicatePath = propertyPath;
                    return true;
                }

                if (TryFindDuplicateProperty(
                        property.Value,
                        propertyPath,
                        out duplicatePath))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind ==
                 JsonValueKind.Array)
        {
            var index = 0;

            foreach (var item
                     in element.EnumerateArray())
            {
                if (TryFindDuplicateProperty(
                        item,
                        $"{path}[{index}]",
                        out duplicatePath))
                {
                    return true;
                }

                index++;
            }
        }

        duplicatePath = string.Empty;
        return false;
    }
}
