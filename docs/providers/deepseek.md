# DeepSeek provider guide

DeepSeek exposes both OpenAI-compatible and Claude-compatible APIs. Baize can
use either style without leaking either provider's wire types into application
code. Live verification currently covers `deepseek-v4-flash` and
`deepseek-v4-pro`.

## Recommended OpenAI-compatible setup

Install `Penghou.Baize.OpenAi`, then select the `DeepSeek` dialect. The dialect
is important: it enables DeepSeek's thinking wire shape, reasoning replay,
parallel tool calls, and tool-backed JSON-schema output.

```json
{
  "LlmRouting": {
    "ProviderModules": [
      { "Assembly": "Penghou.Baize.OpenAi" }
    ],
    "Models": [
      {
        "Name": "deepseek-flash",
        "Endpoints": [
          {
            "Provider": "OpenAi",
            "ProviderModel": "deepseek-v4-flash",
            "BaseUrl": "https://api.deepseek.com/v1",
            "ApiKeySecretName": "DEEPSEEK_API_KEY",
            "Dialect": "DeepSeek"
          }
        ]
      }
    ]
  }
}
```

`ApiKeySecretName` is resolved by Baize's configured `ISecretProvider`. It is
not required to be an environment-variable name.

## Structured output

DeepSeek's OpenAI-compatible API supports `json_object`, but currently rejects
`response_format.type = json_schema`. Baize handles the two cases differently:

- `LlmResponseFormat.Json()` sends `json_object` and adds the provider-required
  instruction to return only valid JSON. Describe the desired shape in your
  own prompt and validate the result locally.
- `LlmResponseFormat.JsonSchema(schema)` declares the schema through a forced
  synthetic `structured_output` function. Baize repackages its arguments as
  ordinary `LlmResponse.Content`; the implementation detail does not leak as a
  tool call. DeepSeek rejects forced tool selection while thinking is active,
  so Baize disables provider-default thinking for this fallback only. An
  explicit request combining thinking and schema output is rejected rather
  than silently ignoring the caller's thinking requirement.

Ordinary tools and tool-backed structured output cannot share one request,
because both would compete for tool selection. Baize rejects that combination
before sending it.

## Strict tool arguments

DeepSeek's strict tool validation is an explicit beta feature. Configure a
separate endpoint using `https://api.deepseek.com/beta`, advertise the
capability, and opt in per tool:

```json
{
  "Provider": "OpenAi",
  "ProviderModel": "deepseek-v4-flash",
  "BaseUrl": "https://api.deepseek.com/beta",
  "ApiKeySecretName": "DEEPSEEK_API_KEY",
  "Dialect": "DeepSeek",
  "Capabilities": {
    "StrictToolArguments": true
  }
}
```

```csharp
var tool = new LlmTool(
    "lookup_order",
    "Looks up an order",
    schemaJson,
    Strict: true);
```

Baize preserves the supplied schema. It does not rewrite it to a hard-coded
DeepSeek subset because live behavior has already differed from the published
subset. Requests for strict tools are rejected unless the selected endpoint
explicitly advertises `StrictToolArguments`.

## Claude-compatible alternative

Install `Penghou.Baize.Claude` and use the `/anthropic` base URL:

```json
{
  "Provider": "Claude",
  "ProviderModel": "deepseek-v4-pro",
  "BaseUrl": "https://api.deepseek.com/anthropic",
  "ApiKeySecretName": "DEEPSEEK_API_KEY"
}
```

The tested chat, tools, parallel tools, thinking, and tool-backed structured
output behavior is equivalent at Baize's canonical API surface. The wire
protocol and available provider diagnostics still differ.

## Troubleshooting and evidence

Enable bounded HTTP diagnostics only while investigating provider behavior;
captured prompts and responses may contain sensitive data. See
[validation and troubleshooting](../validation-and-troubleshooting.md), the
[current compatibility matrix](../live-provider-compatibility.md), and the
[chronological verification log](../live-provider-verification-log.md).

Provider documentation:

- [JSON output](https://api-docs.deepseek.com/guides/json_mode)
- [Tool calls and strict mode](https://api-docs.deepseek.com/guides/tool_calls)
- [Anthropic API compatibility](https://api-docs.deepseek.com/guides/anthropic_api)
