using FluentAssertions;
using Penghou.Baize.Generation;

namespace Penghou.Baize.Tests;

/// <summary>
/// Exercises the common generation request validator, the normalized failure
/// classification, and generated-asset source guards.
/// </summary>
public sealed class GenerationValidationTests
{
    private const string Endpoint = "unit endpoint";

    private static GenerationCapabilities Capabilities(
        GenerationFeature features,
        LlmContentTransport transports = LlmContentTransport.Uri,
        GenerationConstraints? constraints = null,
        int? maximumCandidates = null) =>
        new()
        {
            Features = features,
            InputTransports = new HashSet<LlmContentTransport> { transports },
            MaximumCandidates = maximumCandidates,
            Constraints = constraints
        };

    private static void ShouldFailWith(
        Action act,
        GenerationErrorKind kind,
        string becauseFragment)
    {
        var exception = act.Should().Throw<BaizeException>().Which;
        exception.ErrorKind.Should().Be(kind);
        exception.Message.Should().Contain(Endpoint);
        if (becauseFragment.Length > 0)
            exception.Message.Should().Contain(becauseFragment);
    }

    // ---------- image ----------

    [Fact]
    public void Image_TextToImage_WhenSupported_Passes()
    {
        var act = () => GenerationRequestValidator.Validate(
            Capabilities(GenerationFeature.TextToImage),
            new ImageGenerationRequest { Prompt = "sunset" },
            Endpoint);

        act.Should().NotThrow();
    }

    [Fact]
    public void Image_EditWithoutFeature_IsRejected()
    {
        var act = () => GenerationRequestValidator.Validate(
            Capabilities(GenerationFeature.TextToImage, transports: LlmContentTransport.InlineData),
            new ImageGenerationRequest
            {
                Prompt = "edit",
                Inputs = [new LlmInlineDataSource(new byte[] { 1, 2, 3 })]
            },
            Endpoint);

        ShouldFailWith(act, GenerationErrorKind.UnsupportedCapability, "ImageToImage");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Image_CountBelowOne_IsInvalid(int count)
    {
        var act = () => GenerationRequestValidator.Validate(
            Capabilities(GenerationFeature.TextToImage),
            new ImageGenerationRequest { Prompt = "p", Count = count },
            Endpoint);

        ShouldFailWith(act, GenerationErrorKind.InvalidRequest, $"Count {count}");
    }

    [Fact]
    public void Image_MultipleCandidates_WithoutFeature_IsUnsupported()
    {
        var act = () => GenerationRequestValidator.Validate(
            Capabilities(GenerationFeature.TextToImage),
            new ImageGenerationRequest { Prompt = "p", Count = 2 },
            Endpoint);

        ShouldFailWith(act, GenerationErrorKind.UnsupportedCapability, "multiple image candidates");
    }

    [Fact]
    public void Image_CandidatesAboveMaximum_IsInvalid()
    {
        var act = () => GenerationRequestValidator.Validate(
            Capabilities(
                GenerationFeature.TextToImage | GenerationFeature.MultipleCandidates,
                maximumCandidates: 3),
            new ImageGenerationRequest { Prompt = "p", Count = 4 },
            Endpoint);

        ShouldFailWith(act, GenerationErrorKind.InvalidRequest, "at most 3");
    }

    [Fact]
    public void Image_InputTransportMismatch_IsUnsupported()
    {
        var act = () => GenerationRequestValidator.Validate(
            Capabilities(GenerationFeature.ImageToImage), // only Uri transport
            new ImageGenerationRequest
            {
                Prompt = "p",
                Inputs = [new LlmInlineDataSource(new byte[] { 1 })]
            },
            Endpoint);

        ShouldFailWith(act, GenerationErrorKind.UnsupportedCapability, "InlineData");
    }

    [Fact]
    public void Image_SizeConstraintViolation_IsUnsupported()
    {
        var capabilities = Capabilities(
            GenerationFeature.TextToImage,
            constraints: new GenerationConstraints
            {
                SupportedImageSizes =
                    new HashSet<GenerationImageSize> { new(1024, 1024) }
            });

        var act = () => GenerationRequestValidator.Validate(
            capabilities,
            new ImageGenerationRequest { Prompt = "p", Size = new GenerationImageSize(512, 512) },
            Endpoint);

        ShouldFailWith(act, GenerationErrorKind.UnsupportedCapability, "512x512");
    }

    [Fact]
    public void Image_AspectRatioAndFormatConstraints_AreEnforced()
    {
        var constraints = new GenerationConstraints
        {
            SupportedAspectRatios = new HashSet<string> { "1:1" },
            SupportedOutputFormats = new HashSet<string> { "png" }
        };
        var capabilities = Capabilities(
            GenerationFeature.TextToImage,
            constraints: constraints);

        var ratio = () => GenerationRequestValidator.Validate(
            capabilities,
            new ImageGenerationRequest { Prompt = "p", AspectRatio = "16:9" },
            Endpoint);
        ShouldFailWith(ratio, GenerationErrorKind.UnsupportedCapability, "16:9");

        var format = () => GenerationRequestValidator.Validate(
            capabilities,
            new ImageGenerationRequest { Prompt = "p", OutputFormat = "webp" },
            Endpoint);
        ShouldFailWith(format, GenerationErrorKind.UnsupportedCapability, "webp");
    }

    [Fact]
    public void Image_TooManyInputs_IsInvalid()
    {
        var capabilities = Capabilities(
            GenerationFeature.ImageToImage,
            constraints: new GenerationConstraints { MaximumInputs = 1 });

        var act = () => GenerationRequestValidator.Validate(
            capabilities,
            new ImageGenerationRequest
            {
                Prompt = "p",
                Inputs =
                [
                    new LlmUriSource(new Uri("https://unit.test/a.png")),
                    new LlmUriSource(new Uri("https://unit.test/b.png"))
                ]
            },
            Endpoint);

        ShouldFailWith(act, GenerationErrorKind.InvalidRequest, "at most 1 image inputs");
    }

    [Fact]
    public void Image_MaximumInputsConstraint_WithinLimit_Passes()
    {
        var capabilities = Capabilities(
            GenerationFeature.ImageToImage,
            constraints: new GenerationConstraints { MaximumInputs = 2 });

        var act = () => GenerationRequestValidator.Validate(
            capabilities,
            new ImageGenerationRequest
            {
                Prompt = "p",
                Inputs = [new LlmUriSource(new Uri("https://unit.test/a.png"))]
            },
            Endpoint);

        act.Should().NotThrow();
    }

    // ---------- video ----------

    [Fact]
    public void Video_DurationConstraints_AreEnforced()
    {
        var constraints = new GenerationConstraints
        {
            MinimumDuration = TimeSpan.FromSeconds(4),
            MaximumDuration = TimeSpan.FromSeconds(10)
        };
        var capabilities = Capabilities(
            GenerationFeature.TextToVideo,
            constraints: constraints);

        var tooShort = () => GenerationRequestValidator.Validate(
            capabilities,
            new VideoGenerationRequest { Prompt = "p", Duration = TimeSpan.FromSeconds(2) },
            Endpoint);
        ShouldFailWith(tooShort, GenerationErrorKind.InvalidRequest, "at least");

        var tooLong = () => GenerationRequestValidator.Validate(
            capabilities,
            new VideoGenerationRequest { Prompt = "p", Duration = TimeSpan.FromSeconds(12) },
            Endpoint);
        ShouldFailWith(tooLong, GenerationErrorKind.InvalidRequest, "at most");
    }

    [Fact]
    public void Video_VideoSizeConstraint_IsEnforced()
    {
        var capabilities = Capabilities(
            GenerationFeature.TextToVideo,
            constraints: new GenerationConstraints
            {
                SupportedVideoSizes = new HashSet<GenerationVideoSize> { new(1920, 1080) }
            });

        var act = () => GenerationRequestValidator.Validate(
            capabilities,
            new VideoGenerationRequest { Prompt = "p", Size = new GenerationVideoSize(640, 480) },
            Endpoint);

        ShouldFailWith(act, GenerationErrorKind.UnsupportedCapability, "640x480");
    }

    [Fact]
    public void Video_ImageToVideo_FeatureSelection()
    {
        var act = () => GenerationRequestValidator.Validate(
            Capabilities(GenerationFeature.ImageToVideo),
            new VideoGenerationRequest
            {
                Prompt = "p",
                FirstFrame = new LlmUriSource(new Uri("https://unit.test/f.png"))
            },
            Endpoint);

        act.Should().NotThrow();
    }

    // ---------- audio ----------

    [Theory]
    [InlineData(AudioGenerationKind.Speech, GenerationFeature.TextToSpeech)]
    [InlineData(AudioGenerationKind.SoundEffect, GenerationFeature.TextToSound)]
    [InlineData(AudioGenerationKind.Music, GenerationFeature.TextToMusic)]
    [InlineData(AudioGenerationKind.Transform, GenerationFeature.AudioTransform)]
    public void Audio_KindMapsToRequiredFeature(
        AudioGenerationKind kind,
        GenerationFeature feature)
    {
        var act = () => GenerationRequestValidator.Validate(
            Capabilities(feature),
            new AudioGenerationRequest { Prompt = "narrate", Kind = kind },
            Endpoint);

        act.Should().NotThrow();
    }

    [Fact]
    public void Audio_UnsupportedKind_IsRejected()
    {
        var capabilities = Capabilities(
            GenerationFeature.TextToSpeech,
            constraints: new GenerationConstraints
            {
                SupportedAudioKinds = new HashSet<AudioGenerationKind> { AudioGenerationKind.Speech }
            });

        var act = () => GenerationRequestValidator.Validate(
            capabilities,
            new AudioGenerationRequest { Prompt = "p", Kind = AudioGenerationKind.Music },
            Endpoint);

        ShouldFailWith(act, GenerationErrorKind.UnsupportedCapability, "Music");
    }

    [Fact]
    public void Audio_TransformRequiresSourceAudioTransport()
    {
        var act = () => GenerationRequestValidator.Validate(
            Capabilities(GenerationFeature.AudioTransform), // Uri transport only
            new AudioGenerationRequest
            {
                Prompt = "clean up",
                Kind = AudioGenerationKind.Transform,
                SourceAudio = new LlmInlineDataSource(new byte[] { 9 })
            },
            Endpoint);

        ShouldFailWith(act, GenerationErrorKind.UnsupportedCapability, "InlineData");
    }

    // ---------- dispatch ----------

    private sealed record UnknownGenerationRequest : GenerationRequest;

    [Fact]
    public void Validate_UnknownRequestType_IsInvalid()
    {
        var act = () => GenerationRequestValidator.Validate(
            Capabilities(GenerationFeature.TextToImage),
            new UnknownGenerationRequest(),
            Endpoint);

        ShouldFailWith(act, GenerationErrorKind.InvalidRequest, "cannot handle generation request type");
    }

    [Fact]
    public void Validate_NullArguments_AreRejected()
    {
        var capabilities = Capabilities(GenerationFeature.TextToImage);
        var request = new ImageGenerationRequest { Prompt = "p" };

        var nullCaps = () => GenerationRequestValidator.Validate(null!, request, Endpoint);
        nullCaps.Should().Throw<ArgumentNullException>();

        var nullRequest = () => GenerationRequestValidator.Validate(capabilities, null!, Endpoint);
        nullRequest.Should().Throw<ArgumentNullException>();
    }

    // ---------- BaizeException classification ----------

    [Theory]
    [InlineData(401, GenerationErrorKind.Authentication)]
    [InlineData(403, GenerationErrorKind.Authorization)]
    [InlineData(429, GenerationErrorKind.RateLimited)]
    [InlineData(402, GenerationErrorKind.QuotaExceeded)]
    [InlineData(408, GenerationErrorKind.ProviderUnavailable)]
    [InlineData(400, GenerationErrorKind.InvalidRequest)]
    [InlineData(404, GenerationErrorKind.InvalidRequest)]
    [InlineData(405, GenerationErrorKind.InvalidRequest)]
    [InlineData(422, GenerationErrorKind.InvalidRequest)]
    [InlineData(500, GenerationErrorKind.ProviderUnavailable)]
    [InlineData(503, GenerationErrorKind.ProviderUnavailable)]
    [InlineData(418, GenerationErrorKind.InvalidRequest)]
    public void ClassifyStatusCode_MapsProviderStatuses(int status, GenerationErrorKind expected) =>
        BaizeException.ClassifyStatusCode(status).Should().Be(expected);

    [Fact]
    public void FactoryMethods_SetErrorKindAndInner()
    {
        var inner = new InvalidOperationException("root");

        BaizeException.UnsupportedCapability("a").ErrorKind
            .Should().Be(GenerationErrorKind.UnsupportedCapability);
        BaizeException.InvalidRequest("b").ErrorKind
            .Should().Be(GenerationErrorKind.InvalidRequest);

        var unknown = BaizeException.UnknownSubmissionOutcome("c", inner);
        unknown.ErrorKind.Should().Be(GenerationErrorKind.UnknownSubmissionOutcome);
        unknown.InnerException.Should().BeSameAs(inner);

        var unavailable = BaizeException.ProviderUnavailable("d");
        unavailable.ErrorKind.Should().Be(GenerationErrorKind.ProviderUnavailable);

        var full = new BaizeException(
            "e",
            GenerationErrorKind.RateLimited,
            statusCode: 429,
            providerStatus: "raw",
            innerException: inner);
        full.StatusCode.Should().Be(429);
        full.ProviderStatus.Should().Be("raw");
    }

    // ---------- asset source guards ----------

    [Fact]
    public void GeneratedAssetSources_ValidateArguments()
    {
        var relativeUri = () => new UriGeneratedAssetSource(new Uri("/relative", UriKind.Relative));
        relativeUri.Should().Throw<ArgumentException>();

        var absolute = new UriGeneratedAssetSource(new Uri("https://unit.test/a.png"));
        absolute.Uri.Should().Be(new Uri("https://unit.test/a.png"));

        var emptyData = () => new InlineGeneratedAssetSource(ReadOnlyMemory<byte>.Empty);
        emptyData.Should().Throw<ArgumentException>();

        var inline = new InlineGeneratedAssetSource(new byte[] { 1, 2 }, "audio/mpeg");
        inline.Data.ToArray().Should().Equal(1, 2);
        inline.ContentType.Should().Be("audio/mpeg");

        var blankId = () => new ProviderGeneratedAssetSource(" ");
        blankId.Should().Throw<ArgumentException>();

        var file = new ProviderGeneratedAssetSource("file-7", "runway");
        file.ProviderFileId.Should().Be("file-7");
        file.Provider.Should().Be("runway");
    }
}
