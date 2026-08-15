# Gemini provider guide

`Penghou.Baize.Gemini` targets Google's native Gemini API. Live verification
currently covers Gemini 3.6 Flash, 3.5 Flash, 3.5 Flash-Lite, 3.1 Flash-Lite,
and 3.1 Pro Preview. Availability and account tiers vary by model and project, so
configure capabilities for the exact endpoint rather than assuming every
Gemini model is interchangeable.

Model discovery is not sufficient proof of compatibility. In the tested paid
project, the Models API listed Gemini 2.5 Flash, Flash-Lite, and Pro as
supporting generation, while each legacy `generateContent` request returned a
404 directing new users to the Interactions API. Baize's current Gemini client
targets `generateContent`; Interactions API support would be a separate API
style and should not be inferred from the discovery result.

## Setup

```json
{
  "LlmRouting": {
    "ProviderModules": [
      { "Assembly": "Penghou.Baize.Gemini" }
    ],
    "Models": [
      {
        "Name": "gemini-flash",
        "Endpoints": [
          {
            "Provider": "Gemini",
            "ProviderModel": "gemini-3.6-flash",
            "ApiKeySecretName": "GEMINI_API_KEY"
          }
        ]
      }
    ]
  }
}
```

The provider's default base URL targets the native `v1beta` API. Override it
only for a compatible gateway or a deliberately selected API version.

## JSON Schema adaptation

Gemini's accepted schema dialect is narrower than canonical JSON Schema. In
live tests, Gemini rejected `additionalProperties` in both tool schemas and
structured-output schemas. Gemini's function-declaration schema also rejects
canonical `$ref` references. The Gemini adapter non-lossily inlines resolvable
local references, removes the now-unused `$defs`/`definitions`, and removes
unsupported keywords from the wire copy while preserving the canonical schema
used by the caller and local validation. Recursive or external references are
rejected locally with an actionable validation error because they cannot be
represented safely in Gemini's native schema dialect.

This is intentionally provider-owned behavior. Do not pre-trim application
schemas for Gemini: doing so would weaken validation for every other provider
and make future adapter improvements harder to adopt.

## Multimodal input

The native adapter supports capability-gated image, audio, video, and file
parts. Declare both content type and transport for the exact model:

```csharp
endpoint.ConfigureCapabilities(capabilities => capabilities
    .SupportsContent(LlmContentType.Image, LlmContentTransport.InlineData)
    .SupportsContent(LlmContentType.Audio, LlmContentTransport.InlineData)
    .SupportsContent(LlmContentType.Video, LlmContentTransport.InlineData)
    .SupportsContent(LlmContentType.File, LlmContentTransport.InlineData));
```

The router filters incompatible endpoints before selection and the Gemini
client validates again before transmission. Image generation is deliberately
not exposed through `ILlmClient`; it belongs to the planned GenerationClient,
whose result can represent binary artifacts and long-running operations.

A paid provider-level probe verified `gemini-3.1-flash-lite-image` through the
Interactions API. `POST /v1beta/interactions` returned a MIME-typed, decodable
binary image artifact. This is compatibility evidence for the Gemini
GenerationClient provider; it is not a hidden image-output feature of the
current chat client.

## Image generation

`Penghou.Baize.Gemini` exposes Gemini image generation through
`IGenerationClient` using the Interactions API. Register an endpoint with an
image-capable model:

```csharp
services.AddHttpClient();
services.AddBaizeGeminiGeneration("image", options =>
{
    options.ApiKey = apiKey;
    options.Model = "gemini-3.1-flash-lite-image";
    options.ImageSize = "1K";
});
```

Then submit text-to-image or image-to-image requests through the keyed client:

```csharp
var client = provider.GetRequiredKeyedService<IGenerationClient>("image");

var operation = await client.SubmitAsync(new ImageGenerationRequest
{
    Prompt = "A simple flat icon of one blue circle on a white background.",
    AspectRatio = "1:1",
    OutputFormat = "png"
});
```

Image generation is synchronous: a completed interaction returns inline image
assets. Image editing accepts inline or public-URL input images; inline inputs
assume the configured `DefaultInputImageMimeType` (`image/png` by default)
because the common request model carries no content type. Operation retrieval
and cancellation are rejected before a provider call because the synchronous
interaction path has no task lifecycle.

## Thinking controls

Thinking behavior is model-specific. For example, live testing found that
Gemini 3.6 Flash rejects `thinkingBudget: 0`; an endpoint for that model should
therefore advertise `ThinkingDisable = false`. Thinking tokens also consume
the response token allowance, so tool requests need enough output budget to
reach the function call after reasoning.

## Batch and account tiers

Native batch now passes end to end on the paid tier for all currently
available tested Flash models and 3.1 Pro Preview. Current v1beta operations return the result reference
as `response.responsesFile`, and Baize retrieves it through Gemini's download
endpoint. A one-item batch still took roughly two to four minutes in live
tests, so use it for durable throughput and cost optimization, not interactive
responses. Keep capabilities conservative for model/tier combinations that
have not passed a live test.

## Troubleshooting and evidence

See [validation and troubleshooting](../validation-and-troubleshooting.md),
the [current compatibility matrix](../live-provider-compatibility.md), and the
[chronological verification log](../live-provider-verification-log.md). The
integration suite can run one capability category at a time, avoiding token
spend on already verified behavior.

Current Google references:

- [Gemini models](https://ai.google.dev/gemini-api/docs/models)
- [Interactions API](https://ai.google.dev/gemini-api/docs/interactions-overview)
- [Gemini image generation](https://ai.google.dev/gemini-api/docs/image-generation)
- [Gemini API pricing](https://ai.google.dev/gemini-api/docs/pricing)
