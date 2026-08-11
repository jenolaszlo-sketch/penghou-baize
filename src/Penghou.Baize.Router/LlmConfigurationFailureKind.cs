namespace Penghou.Baize.Router;

/// <summary>Identifies the configuration stage that failed.</summary>
public enum LlmConfigurationFailureKind
{
    /// <summary>The option graph is internally inconsistent.</summary>
    Structural,
    /// <summary>A provider client, endpoint, or secret could not initialize.</summary>
    EndpointInitialization
}
