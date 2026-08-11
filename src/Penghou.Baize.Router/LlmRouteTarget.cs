namespace Penghou.Baize.Router;

/// <summary>A validated model, strategy, or named-route routing target.</summary>
public sealed record LlmRouteTarget
{
    private LlmRouteTarget(
        LlmRouteKind kind,
        string? name,
        ModelStrategy? strategy)
    {
        Kind = kind;
        Name = name;
        Strategy = strategy;
    }

    /// <summary>The target category.</summary>
    public LlmRouteKind Kind { get; }

    /// <summary>The model or named-route identifier, when applicable.</summary>
    public string? Name { get; }

    /// <summary>The typed strategy, when applicable.</summary>
    public ModelStrategy? Strategy { get; }

    /// <summary>Creates a direct logical-model target.</summary>
    public static LlmRouteTarget Model(string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        return new LlmRouteTarget(LlmRouteKind.Model, model, null);
    }

    /// <summary>Creates a built-in strategy target.</summary>
    public static LlmRouteTarget ForStrategy(ModelStrategy strategy) =>
        new(LlmRouteKind.Strategy, null, strategy);

    /// <summary>Creates an application-defined named-route target.</summary>
    public static LlmRouteTarget Named(string route)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        return new LlmRouteTarget(LlmRouteKind.Named, route, null);
    }

    /// <inheritdoc />
    public override string ToString() => Kind switch
    {
        LlmRouteKind.Model => $"model:{Name}",
        LlmRouteKind.Strategy => $"strategy:{Strategy}",
        LlmRouteKind.Named => $"route:{Name}",
        _ => Kind.ToString()
    };
}
