namespace Penghou.Baize.Tools;

/// <summary>Controls how structured-output repair interacts with streaming.</summary>
public sealed class StructuredOutputRepairOptions
{
    /// <summary>
    /// When true (the default), schema-constrained streams are buffered so
    /// repaired JSON can be emitted atomically. Set to false to retain native
    /// provider streaming; completion APIs are still repaired.
    /// </summary>
    public bool BufferStreamingResponses { get; set; } = true;
}
