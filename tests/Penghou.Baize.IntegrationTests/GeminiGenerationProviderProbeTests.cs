using FluentAssertions;
using System.Net.Http.Json;
using System.Text.Json;

namespace Penghou.Baize.IntegrationTests;

public sealed class GeminiGenerationProviderProbeTests(ITestOutputHelper output)
{
    [Fact]
    [Trait(LiveTestTraits.Category, LiveTestTraits.Live)]
    [Trait(LiveTestTraits.Capability, LiveTestTraits.ImageGeneration)]
    public async Task ImageGenerationProviderProbe_ReturnsBinaryImage()
    {
        var settings = LiveTestSettings.Load();
        if (!LiveTestSettings.ImageGenerationEnabled)
            Assert.Skip("Set BAIZE_LIVE_TEST_IMAGE_GENERATION=1 for the paid image-generation probe.");
        if (!settings.Provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
            Assert.Skip("The current provider-level image-generation probe supports Gemini only.");

        var apiKey = Environment.GetEnvironmentVariable(settings.SecretName!);
        apiKey.Should().NotBeNullOrWhiteSpace();

        using var client = new HttpClient { Timeout = settings.HttpTimeout };
        client.DefaultRequestHeaders.Add("x-goog-api-key", apiKey);

        using var response = await client.PostAsJsonAsync(
            "https://generativelanguage.googleapis.com/v1beta/interactions",
            new
            {
                model = LiveTestSettings.ImageGenerationModel,
                input = new[]
                {
                    new
                    {
                        type = "text",
                        text = "Create a simple flat icon of one blue circle centered on a white background. No text."
                    }
                },
                store = false
            },
            TestContext.Current.CancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        response.IsSuccessStatusCode.Should().BeTrue(
            because: $"Gemini returned {(int)response.StatusCode}: {responseBody}");

        using var document = JsonDocument.Parse(responseBody);
        var image = Assert.NotNull(FindImageData(document.RootElement));
        image.MimeType.Should().StartWith("image/");

        var bytes = Convert.FromBase64String(image.Data);
        bytes.Should().NotBeEmpty();
        output.WriteLine(
            $"Provider=Gemini Model={LiveTestSettings.ImageGenerationModel} " +
            $"MimeType={image.MimeType} Bytes={bytes.Length}");
    }

    private static (string MimeType, string Data)? FindImageData(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            string? type = null;
            string? mimeType = null;
            string? data = null;
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("type") && property.Value.ValueKind == JsonValueKind.String)
                    type = property.Value.GetString();
                else if ((property.NameEquals("mime_type") || property.NameEquals("mimeType")) &&
                         property.Value.ValueKind == JsonValueKind.String)
                    mimeType = property.Value.GetString();
                else if (property.NameEquals("data") && property.Value.ValueKind == JsonValueKind.String)
                    data = property.Value.GetString();
            }

            if (type is "image" && mimeType is not null && data is not null)
                return (mimeType, data);

            foreach (var property in element.EnumerateObject())
            {
                var nested = FindImageData(property.Value);
                if (nested is not null)
                    return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindImageData(item);
                if (nested is not null)
                    return nested;
            }
        }

        return null;
    }
}
