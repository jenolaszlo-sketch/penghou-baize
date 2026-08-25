namespace Penghou.Baize;

/// <summary>Well-known endpoint setting names understood by Baize providers.</summary>
public static class LlmSettingNames
{
    /// <summary>The OpenAI-compatible wire dialect.</summary>
    public const string Dialect = "Dialect";

    /// <summary>The Claude thinking-wire style.</summary>
    public const string ThinkingStyle = "ThinkingStyle";
}

/// <summary>Reserved tool names used by provider-neutral Baize protocols.</summary>
public static class LlmProtocolNames
{
    /// <summary>The synthetic tool used to carry schema-constrained output.</summary>
    public const string StructuredOutputTool = "structured_output";
}
