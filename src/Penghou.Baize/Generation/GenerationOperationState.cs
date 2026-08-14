namespace Penghou.Baize.Generation;

/// <summary>The lifecycle state of a generation operation.</summary>
public enum GenerationOperationState
{
    /// <summary>The provider state could not be mapped deterministically.</summary>
    Unknown,

    /// <summary>The operation has been accepted but has not started.</summary>
    Queued,

    /// <summary>The operation is actively generating.</summary>
    Running,

    /// <summary>The operation completed and produced a result.</summary>
    Succeeded,

    /// <summary>The provider reported a terminal generation failure.</summary>
    Failed,

    /// <summary>The provider canceled the operation through <see cref="IGenerationClient.CancelAsync"/>.</summary>
    Canceled
}