namespace Penghou.Baize;

/// <summary>The kinds of content a message part can carry.</summary>
public enum LlmContentType
{
    /// <summary>Plain text.</summary>
    Text,

    /// <summary>An image (inline bytes or reference).</summary>
    Image,

    /// <summary>An audio clip.</summary>
    Audio,

    /// <summary>A video clip.</summary>
    Video,

    /// <summary>A generic file attachment.</summary>
    File
}
