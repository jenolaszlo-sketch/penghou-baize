# OpenAI provider guide

`Penghou.Baize.OpenAi` targets OpenAI's native API for conversational
completion (`OpenAiChatClient`) and explicit artifact generation
(`OpenAiGenerationClient`). Explicit artifact requests are never routed through
chat; the chat client is conversation-only and the generation client is the
recommended surface for creating, editing, or transforming media.

## Chat setup

```json
{
  "LlmRouting": {
    "ProviderModules": [
      { "Assembly": "Penghou.Baize.OpenAi" }
    ],
    "Models": [
      {
        "Name": "gpt",
        "Endpoints": [
          {
            "Provider": "OpenAi",
            "ProviderModel": "gpt-4o",
            "ApiKeySecretName": "OPENAI_API_KEY"
          }
        ]
      }
    ]
  }
}
```

The provider's default base address is `https://api.openai.com/v1`. Override it
with `BAIZE_LIVE_BASE_URL` or the endpoint configuration for compatible
gateways.

## Artifact generation

`AddBaizeOpenAiGeneration` registers an `IGenerationClient` endpoint. One
options instance maps to one endpoint; register multiple endpoints under
distinct identifiers when different models are required.

```csharp
services.AddBaizeOpenAiGeneration("images", options =>
{
    options.ApiKey = apiKey;
    options.Model = "gpt-image-1";
    options.Features =
        GenerationFeature.TextToImage |
        GenerationFeature.ImageToImage |
        GenerationFeature.MultipleCandidates;
});
```

The OpenAI adapter implements four modalities:

| Modality | Wire endpoint | Returns |
| --- | --- | --- |
| Text-to-image | `POST /images/generations` | `Succeeded` with image assets |
| Image editing | `POST /images/edits` | `Succeeded` with image assets |
| Video (Sora) | `POST /videos`, `GET /videos/{id}`, `DELETE /videos/{id}` | queued operation, polled with `GetAsync` |
| Speech | `POST /audio/speech` | `Succeeded` with inline audio |

### Text-to-image

```csharp
var operation = await client.SubmitAsync(new ImageGenerationRequest
{
    Prompt = "a flat blue-circle icon on a white background",
    Count = 2,
    Size = new GenerationImageSize(1024, 1024)
});
```

### Image editing

Editing is selected by supplying one or more image inputs. Inline bytes are
posted as multipart image parts; absolute URIs are posted as reference
identifiers for OpenAI to fetch.

```csharp
var operation = await client.SubmitAsync(new ImageGenerationRequest
{
    Prompt = "Turn the red shape blue.",
    Inputs = [new LlmInlineDataSource(imageBytes)]
});
```

### Video (queued)

Video submission returns a `Queued` operation with a pinned handle. Poll
`GetAsync` (for example through `IGenerationExecutor`) until it reaches
`Succeeded`, or cancel it with `CancelAsync` (`DELETE /videos/{id}`). The
endpoint advertises `OperationRetrieval`, `Cancellation`, and `Progress`.

```csharp
var operation = await client.SubmitAsync(new VideoGenerationRequest
{
    Prompt = "A slow pan across a calm ocean at sunset."
});
```

### Speech

Speech returns immediately with an inline audio asset.

```csharp
var operation = await client.SubmitAsync(new AudioGenerationRequest
{
    Prompt = "Hello from Baize.",
    OutputFormat = "mp3"
});
```

### Per-modality models

`OpenAiGenerationOptions` exposes `ImageModel`, `VideoModel`, and `AudioModel`
overrides so one endpoint can target the appropriate model for each modality
while `Model` remains the endpoint's default and the operation-handle model.

### Live verification

The opt-in generation probe drives `IGenerationClient` through DI against the
real API (see the README live-test section). Set `BAIZE_LIVE_PROVIDER=OpenAi`
and `BAIZE_LIVE_TEST_GENERATION=1`, then select each modality model with
`BAIZE_LIVE_GENERATION_MODEL`, `BAIZE_LIVE_GENERATION_VIDEO_MODEL`, and
`BAIZE_LIVE_GENERATION_AUDIO_MODEL`.