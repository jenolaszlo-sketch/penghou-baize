namespace Penghou.Baize.Generation;

/// <summary>The kind of audio artifact a request asks for.</summary>
public enum AudioGenerationKind
{
    /// <summary>Speech synthesis from text.</summary>
    Speech,

    /// <summary>Sound-effect generation from a description.</summary>
    SoundEffect,

    /// <summary>Music generation from a description.</summary>
    Music,

    /// <summary>Audio transformation / editing.</summary>
    Transform
}
