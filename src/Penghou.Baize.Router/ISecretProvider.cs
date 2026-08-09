namespace Penghou.Baize.Router;

/// <summary>
/// Resolves secrets (for example API keys) by name without coupling the
/// router to a particular source such as process environment variables.
/// </summary>
public interface ISecretProvider
{
    /// <summary>
    /// Resolves the secret registered under <paramref name="name"/>, or
    /// <c>null</c> when no secret with that name is available.
    /// </summary>
    /// <param name="name">The secret's name (for example an environment variable name).</param>
    /// <param name="cancellationToken">Propagates notification that the lookup should be cancelled.</param>
    /// <returns>The secret value, or <c>null</c> when it is not available.</returns>
    Task<string?> GetSecretAsync(string name, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="ISecretProvider"/> backed by the process environment.
/// </summary>
public sealed class EnvironmentSecretProvider : ISecretProvider
{
    /// <inheritdoc />
    public Task<string?> GetSecretAsync(
        string name,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Environment.GetEnvironmentVariable(name));
}
