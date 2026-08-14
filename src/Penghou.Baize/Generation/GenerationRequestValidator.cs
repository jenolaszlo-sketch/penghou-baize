namespace Penghou.Baize.Generation;

/// <summary>
/// Validates a common generation request against the capabilities of the
/// configured endpoint before a billable submission whenever the constraint is
/// statically known. Providers call this first and then apply any additional
/// model-specific constraints.
/// </summary>
public static class GenerationRequestValidator
{
    /// <summary>
    /// Validates <paramref name="request"/> against <paramref name="capabilities"/>,
    /// throwing a <see cref="BaizeException"/> with
    /// <see cref="GenerationErrorKind.UnsupportedCapability"/> or
    /// <see cref="GenerationErrorKind.InvalidRequest"/> on failure.
    /// </summary>
    /// <param name="capabilities">The endpoint capabilities to validate against.</param>
    /// <param name="request">The modality-specific request.</param>
    /// <param name="endpointDescription">A human-readable endpoint label used in failure messages.</param>
    /// <exception cref="ArgumentNullException"><paramref name="capabilities"/> or <paramref name="request"/> is null.</exception>
    /// <exception cref="BaizeException">The request is not supported or invalid for the endpoint.</exception>
    public static void Validate(
        GenerationCapabilities capabilities,
        GenerationRequest request,
        string endpointDescription)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(request);

        switch (request)
        {
            case ImageGenerationRequest image:
                ValidateImage(capabilities, image, endpointDescription);
                break;
            case VideoGenerationRequest video:
                ValidateVideo(capabilities, video, endpointDescription);
                break;
            case AudioGenerationRequest audio:
                ValidateAudio(capabilities, audio, endpointDescription);
                break;
            default:
                throw new BaizeException(
                    $"Endpoint '{endpointDescription}' cannot handle generation request " +
                    $"type '{request.GetType().Name}'.",
                    GenerationErrorKind.InvalidRequest);
        }
    }

    private static void ValidateImage(
        GenerationCapabilities capabilities,
        ImageGenerationRequest request,
        string endpointDescription)
    {
        var isEdit = request.Inputs.Count > 0;
        RequireFeature(
            capabilities,
            isEdit ? GenerationFeature.ImageToImage : GenerationFeature.TextToImage,
            "image",
            endpointDescription);

        if (request.Count < 1)
            throw BaizeException.InvalidRequest(
                $"Endpoint '{endpointDescription}' rejected an image request with Count {request.Count}; Count must be at least 1.");

        if (request.Count > 1 && !capabilities.Supports(GenerationFeature.MultipleCandidates))
            throw BaizeException.UnsupportedCapability(
                $"Endpoint '{endpointDescription}' does not support multiple image candidates.");

        if (capabilities.MaximumCandidates is { } maximum && request.Count > maximum)
            throw BaizeException.InvalidRequest(
                $"Endpoint '{endpointDescription}' supports at most {maximum} image candidates, but " +
                $"{request.Count} were requested.");

        ValidateInputTransport(capabilities, request.Inputs, endpointDescription);
        ValidateConstraints(capabilities, request, endpointDescription);
    }

    private static void ValidateVideo(
        GenerationCapabilities capabilities,
        VideoGenerationRequest request,
        string endpointDescription)
    {
        var feature = request.SourceVideo is not null
            ? GenerationFeature.VideoToVideo
            : request.FirstFrame is not null
                ? GenerationFeature.ImageToVideo
                : GenerationFeature.TextToVideo;
        RequireFeature(capabilities, feature, "video", endpointDescription);

        var constraints = capabilities.Constraints;
        if (constraints is not null && request.Duration is { } duration)
        {
            if (constraints.MinimumDuration is { } minimum && duration < minimum)
                throw BaizeException.InvalidRequest(
                    $"Endpoint '{endpointDescription}' requires at least {minimum} of video, but " +
                    $"{duration} was requested.");
            if (constraints.MaximumDuration is { } maximum && duration > maximum)
                throw BaizeException.InvalidRequest(
                    $"Endpoint '{endpointDescription}' supports at most {maximum} of video, but " +
                    $"{duration} was requested.");
        }

        var inputs = request.References
            .Prepend(request.SourceVideo)
            .Prepend(request.FirstFrame)
            .Prepend(request.LastFrame)
            .Where(source => source is not null)
            .Cast<LlmMediaSource>();
        ValidateInputTransport(capabilities, inputs, endpointDescription);
        ValidateConstraints(capabilities, request, endpointDescription);
    }

    private static void ValidateAudio(
        GenerationCapabilities capabilities,
        AudioGenerationRequest request,
        string endpointDescription)
    {
        var feature = request.Kind switch
        {
            AudioGenerationKind.Speech => GenerationFeature.TextToSpeech,
            AudioGenerationKind.SoundEffect => GenerationFeature.TextToSound,
            AudioGenerationKind.Music => GenerationFeature.TextToMusic,
            AudioGenerationKind.Transform => GenerationFeature.AudioTransform,
            _ => GenerationFeature.None
        };
        RequireFeature(capabilities, feature, "audio", endpointDescription);

        var constraints = capabilities.Constraints;
        if (constraints is not null &&
            constraints.SupportedAudioKinds.Count > 0 &&
            !constraints.SupportedAudioKinds.Contains(request.Kind))
        {
            throw BaizeException.UnsupportedCapability(
                $"Endpoint '{endpointDescription}' does not support audio generation kind '{request.Kind}'.");
        }

        if (constraints is not null && request.Duration is { } duration)
        {
            if (constraints.MinimumDuration is { } minimum && duration < minimum)
                throw BaizeException.InvalidRequest(
                    $"Endpoint '{endpointDescription}' requires at least {minimum} of audio, but " +
                    $"{duration} was requested.");
            if (constraints.MaximumDuration is { } maximum && duration > maximum)
                throw BaizeException.InvalidRequest(
                    $"Endpoint '{endpointDescription}' supports at most {maximum} of audio, but " +
                    $"{duration} was requested.");
        }

        if (request.SourceAudio is not null)
            ValidateInputTransport(capabilities, [request.SourceAudio], endpointDescription);
        ValidateConstraints(capabilities, request, endpointDescription);
    }

    private static void ValidateInputTransport(
        GenerationCapabilities capabilities,
        IEnumerable<LlmMediaSource> inputs,
        string endpointDescription)
    {
        foreach (var input in inputs)
        {
            if (input is null)
                continue;
            if (!capabilities.InputTransports.Contains(input.Transport))
                throw BaizeException.UnsupportedCapability(
                    $"Endpoint '{endpointDescription}' does not accept input transport " +
                    $"'{input.Transport}'.");
        }
    }

    private static void ValidateConstraints(
        GenerationCapabilities capabilities,
        ImageGenerationRequest request,
        string endpointDescription)
    {
        var constraints = capabilities.Constraints;
        if (constraints is null)
            return;

        if (constraints.MaximumInputs is { } maximumInputs && request.Inputs.Count > maximumInputs)
            throw BaizeException.InvalidRequest(
                $"Endpoint '{endpointDescription}' accepts at most {maximumInputs} image inputs, but " +
                $"{request.Inputs.Count} were provided.");

        if (constraints.SupportedImageSizes.Count > 0 && request.Size is { } size &&
            !constraints.SupportedImageSizes.Contains(size))
        {
            throw BaizeException.UnsupportedCapability(
                $"Endpoint '{endpointDescription}' does not support image size " +
                $"'{size.Width}x{size.Height}'.");
        }

        if (constraints.SupportedAspectRatios.Count > 0 && request.AspectRatio is { } ratio &&
            !constraints.SupportedAspectRatios.Contains(ratio))
        {
            throw BaizeException.UnsupportedCapability(
                $"Endpoint '{endpointDescription}' does not support aspect ratio '{ratio}'.");
        }

        if (constraints.SupportedOutputFormats.Count > 0 && request.OutputFormat is { } format &&
            !constraints.SupportedOutputFormats.Contains(format))
        {
            throw BaizeException.UnsupportedCapability(
                $"Endpoint '{endpointDescription}' does not support output format '{format}'.");
        }
    }

    private static void ValidateConstraints(
        GenerationCapabilities capabilities,
        VideoGenerationRequest request,
        string endpointDescription)
    {
        var constraints = capabilities.Constraints;
        if (constraints is null)
            return;

        if (constraints.SupportedVideoSizes.Count > 0 && request.Size is { } size &&
            !constraints.SupportedVideoSizes.Contains(size))
        {
            throw BaizeException.UnsupportedCapability(
                $"Endpoint '{endpointDescription}' does not support video size " +
                $"'{size.Width}x{size.Height}'.");
        }

        if (constraints.SupportedAspectRatios.Count > 0 && request.AspectRatio is { } ratio &&
            !constraints.SupportedAspectRatios.Contains(ratio))
        {
            throw BaizeException.UnsupportedCapability(
                $"Endpoint '{endpointDescription}' does not support aspect ratio '{ratio}'.");
        }
    }

    private static void ValidateConstraints(
        GenerationCapabilities capabilities,
        AudioGenerationRequest request,
        string endpointDescription)
    {
        var constraints = capabilities.Constraints;
        if (constraints is null)
            return;

        if (constraints.SupportedOutputFormats.Count > 0 && request.OutputFormat is { } format &&
            !constraints.SupportedOutputFormats.Contains(format))
        {
            throw BaizeException.UnsupportedCapability(
                $"Endpoint '{endpointDescription}' does not support output format '{format}'.");
        }
    }

    private static void RequireFeature(
        GenerationCapabilities capabilities,
        GenerationFeature feature,
        string modality,
        string endpointDescription)
    {
        if (!capabilities.Supports(feature))
            throw BaizeException.UnsupportedCapability(
                $"Endpoint '{endpointDescription}' does not support '{feature}' " +
                $"({modality} generation).");
    }
}