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

        var failures = Collect(
            capabilities,
            request,
            endpointDescription);
        if (failures.Count > 0)
        {
            throw new BaizeException(
                failures[0].Message,
                failures[0].Kind);
        }
    }

    /// <summary>
    /// Non-throwing form of <see cref="Validate"/> used for capability
    /// probing: routing can test candidate endpoints without paying for
    /// exception construction as a control-flow mechanism.
    /// </summary>
    /// <param name="capabilities">The endpoint capabilities to validate against.</param>
    /// <param name="request">The modality-specific request.</param>
    /// <param name="diagnostics">Human-readable rejection reasons, empty when valid.</param>
    /// <returns><c>true</c> when the endpoint can accept the request.</returns>
    public static bool TryValidate(
        GenerationCapabilities capabilities,
        GenerationRequest request,
        out IReadOnlyList<string> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(request);

        var failures = Collect(
            capabilities,
            request,
            "endpoint");
        diagnostics = failures
            .Select(failure => failure.Message)
            .ToArray();
        return failures.Count == 0;
    }

    private static List<(GenerationErrorKind Kind, string Message)> Collect(
        GenerationCapabilities capabilities,
        GenerationRequest request,
        string endpointDescription)
    {
        var failures = new List<(GenerationErrorKind, string)>();

        switch (request)
        {
            case ImageGenerationRequest image:
                CollectImage(capabilities, image, endpointDescription, failures);
                break;
            case VideoGenerationRequest video:
                CollectVideo(capabilities, video, endpointDescription, failures);
                break;
            case AudioGenerationRequest audio:
                CollectAudio(capabilities, audio, endpointDescription, failures);
                break;
            default:
                failures.Add((
                    GenerationErrorKind.InvalidRequest,
                    $"Endpoint '{endpointDescription}' cannot handle generation request " +
                    $"type '{request.GetType().Name}'."));
                break;
        }

        return failures;
    }

    private static void CollectImage(
        GenerationCapabilities capabilities,
        ImageGenerationRequest request,
        string endpointDescription,
        List<(GenerationErrorKind Kind, string Message)> failures)
    {
        var isEdit = request.Inputs.Count > 0;
        RequireFeature(
            capabilities,
            isEdit ? GenerationFeature.ImageToImage : GenerationFeature.TextToImage,
            "image",
            endpointDescription,
            failures);

        if (request.Count < 1)
        {
            failures.Add((
                GenerationErrorKind.InvalidRequest,
                $"Endpoint '{endpointDescription}' rejected an image request with Count {request.Count}; Count must be at least 1."));
        }

        if (request.Count > 1 && !capabilities.Supports(GenerationFeature.MultipleCandidates))
        {
            failures.Add((
                GenerationErrorKind.UnsupportedCapability,
                $"Endpoint '{endpointDescription}' does not support multiple image candidates."));
        }

        if (capabilities.MaximumCandidates is { } maximum && request.Count > maximum)
        {
            failures.Add((
                GenerationErrorKind.InvalidRequest,
                $"Endpoint '{endpointDescription}' supports at most {maximum} image candidates, but " +
                $"{request.Count} were requested."));
        }

        CollectInputTransport(capabilities, request.Inputs, endpointDescription, failures);
        CollectImageConstraints(capabilities, request, endpointDescription, failures);
    }

    private static void CollectVideo(
        GenerationCapabilities capabilities,
        VideoGenerationRequest request,
        string endpointDescription,
        List<(GenerationErrorKind Kind, string Message)> failures)
    {
        var feature = request.SourceVideo is not null
            ? GenerationFeature.VideoToVideo
            : request.FirstFrame is not null
                ? GenerationFeature.ImageToVideo
                : GenerationFeature.TextToVideo;
        RequireFeature(capabilities, feature, "video", endpointDescription, failures);

        var constraints = capabilities.Constraints;
        if (constraints is not null && request.Duration is { } duration)
        {
            if (constraints.MinimumDuration is { } minimum && duration < minimum)
            {
                failures.Add((
                    GenerationErrorKind.InvalidRequest,
                    $"Endpoint '{endpointDescription}' requires at least {minimum} of video, but " +
                    $"{duration} was requested."));
            }

            if (constraints.MaximumDuration is { } maximum && duration > maximum)
            {
                failures.Add((
                    GenerationErrorKind.InvalidRequest,
                    $"Endpoint '{endpointDescription}' supports at most {maximum} of video, but " +
                    $"{duration} was requested."));
            }
        }

        var inputs = request.References
            .Prepend(request.SourceVideo)
            .Prepend(request.FirstFrame)
            .Prepend(request.LastFrame)
            .Where(source => source is not null)
            .Cast<LlmMediaSource>();
        CollectInputTransport(capabilities, inputs, endpointDescription, failures);
        CollectVideoConstraints(capabilities, request, endpointDescription, failures);
    }

    private static void CollectAudio(
        GenerationCapabilities capabilities,
        AudioGenerationRequest request,
        string endpointDescription,
        List<(GenerationErrorKind Kind, string Message)> failures)
    {
        var feature = request.Kind switch
        {
            AudioGenerationKind.Speech => GenerationFeature.TextToSpeech,
            AudioGenerationKind.SoundEffect => GenerationFeature.TextToSound,
            AudioGenerationKind.Music => GenerationFeature.TextToMusic,
            AudioGenerationKind.Transform => GenerationFeature.AudioTransform,
            _ => GenerationFeature.None
        };
        RequireFeature(capabilities, feature, "audio", endpointDescription, failures);

        var constraints = capabilities.Constraints;
        if (constraints is not null &&
            constraints.SupportedAudioKinds.Count > 0 &&
            !constraints.SupportedAudioKinds.Contains(request.Kind))
        {
            failures.Add((
                GenerationErrorKind.UnsupportedCapability,
                $"Endpoint '{endpointDescription}' does not support audio generation kind '{request.Kind}'."));
        }

        if (constraints is not null && request.Duration is { } duration)
        {
            if (constraints.MinimumDuration is { } minimum && duration < minimum)
            {
                failures.Add((
                    GenerationErrorKind.InvalidRequest,
                    $"Endpoint '{endpointDescription}' requires at least {minimum} of audio, but " +
                    $"{duration} was requested."));
            }

            if (constraints.MaximumDuration is { } maximum && duration > maximum)
            {
                failures.Add((
                    GenerationErrorKind.InvalidRequest,
                    $"Endpoint '{endpointDescription}' supports at most {maximum} of audio, but " +
                    $"{duration} was requested."));
            }
        }

        if (request.SourceAudio is not null)
            CollectInputTransport(capabilities, [request.SourceAudio], endpointDescription, failures);
        CollectAudioConstraints(capabilities, request, endpointDescription, failures);
    }

    private static void CollectInputTransport(
        GenerationCapabilities capabilities,
        IEnumerable<LlmMediaSource> inputs,
        string endpointDescription,
        List<(GenerationErrorKind Kind, string Message)> failures)
    {
        foreach (var input in inputs)
        {
            if (input is null)
                continue;
            if (!capabilities.InputTransports.Contains(input.Transport))
            {
                failures.Add((
                    GenerationErrorKind.UnsupportedCapability,
                    $"Endpoint '{endpointDescription}' does not accept input transport " +
                    $"'{input.Transport}'."));
            }
        }
    }

    private static void CollectImageConstraints(
        GenerationCapabilities capabilities,
        ImageGenerationRequest request,
        string endpointDescription,
        List<(GenerationErrorKind Kind, string Message)> failures)
    {
        var constraints = capabilities.Constraints;
        if (constraints is null)
            return;

        if (constraints.MaximumInputs is { } maximumInputs && request.Inputs.Count > maximumInputs)
        {
            failures.Add((
                GenerationErrorKind.InvalidRequest,
                $"Endpoint '{endpointDescription}' accepts at most {maximumInputs} image inputs, but " +
                $"{request.Inputs.Count} were provided."));
        }

        if (constraints.SupportedImageSizes.Count > 0 && request.Size is { } size &&
            !constraints.SupportedImageSizes.Contains(size))
        {
            failures.Add((
                GenerationErrorKind.UnsupportedCapability,
                $"Endpoint '{endpointDescription}' does not support image size " +
                $"'{size.Width}x{size.Height}'."));
        }

        if (constraints.SupportedAspectRatios.Count > 0 && request.AspectRatio is { } ratio &&
            !constraints.SupportedAspectRatios.Contains(ratio))
        {
            failures.Add((
                GenerationErrorKind.UnsupportedCapability,
                $"Endpoint '{endpointDescription}' does not support aspect ratio '{ratio}'."));
        }

        if (constraints.SupportedOutputFormats.Count > 0 && request.OutputFormat is { } format &&
            !constraints.SupportedOutputFormats.Contains(format))
        {
            failures.Add((
                GenerationErrorKind.UnsupportedCapability,
                $"Endpoint '{endpointDescription}' does not support output format '{format}'."));
        }
    }

    private static void CollectVideoConstraints(
        GenerationCapabilities capabilities,
        VideoGenerationRequest request,
        string endpointDescription,
        List<(GenerationErrorKind Kind, string Message)> failures)
    {
        var constraints = capabilities.Constraints;
        if (constraints is null)
            return;

        if (constraints.SupportedVideoSizes.Count > 0 && request.Size is { } size &&
            !constraints.SupportedVideoSizes.Contains(size))
        {
            failures.Add((
                GenerationErrorKind.UnsupportedCapability,
                $"Endpoint '{endpointDescription}' does not support video size " +
                $"'{size.Width}x{size.Height}'."));
        }

        if (constraints.SupportedAspectRatios.Count > 0 && request.AspectRatio is { } ratio &&
            !constraints.SupportedAspectRatios.Contains(ratio))
        {
            failures.Add((
                GenerationErrorKind.UnsupportedCapability,
                $"Endpoint '{endpointDescription}' does not support aspect ratio '{ratio}'."));
        }
    }

    private static void CollectAudioConstraints(
        GenerationCapabilities capabilities,
        AudioGenerationRequest request,
        string endpointDescription,
        List<(GenerationErrorKind Kind, string Message)> failures)
    {
        var constraints = capabilities.Constraints;
        if (constraints is null)
            return;

        if (constraints.SupportedOutputFormats.Count > 0 && request.OutputFormat is { } format &&
            !constraints.SupportedOutputFormats.Contains(format))
        {
            failures.Add((
                GenerationErrorKind.UnsupportedCapability,
                $"Endpoint '{endpointDescription}' does not support output format '{format}'."));
        }
    }

    private static void RequireFeature(
        GenerationCapabilities capabilities,
        GenerationFeature feature,
        string modality,
        string endpointDescription,
        List<(GenerationErrorKind Kind, string Message)> failures)
    {
        if (!capabilities.Supports(feature))
        {
            failures.Add((
                GenerationErrorKind.UnsupportedCapability,
                $"Endpoint '{endpointDescription}' does not support '{feature}' " +
                $"({modality} generation)."));
        }
    }
}