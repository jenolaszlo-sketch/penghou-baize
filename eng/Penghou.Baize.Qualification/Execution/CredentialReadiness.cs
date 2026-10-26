namespace Penghou.Baize.Qualification.Execution;

internal interface ICredentialReadinessProvider
{
    bool CanProvideNonEmptyCredential(string credentialReference);
}

internal sealed class EnvironmentCredentialReadinessProvider : ICredentialReadinessProvider
{
    public bool CanProvideNonEmptyCredential(string credentialReference) =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(credentialReference));
}

internal static class CredentialReadiness
{
    public static void Require(ICredentialReadinessProvider provider, string credentialReference)
    {
        if (string.IsNullOrWhiteSpace(credentialReference))
            throw new ExecutionException("live-credential-not-ready");
        try
        {
            if (!provider.CanProvideNonEmptyCredential(credentialReference))
                throw new ExecutionException("live-credential-not-ready");
        }
        catch (ExecutionException) { throw; }
        catch (Exception)
        {
            // Provider details can contain sensitive implementation data; only
            // emit a stable non-secret readiness failure code.
            throw new ExecutionException("live-credential-provider-unavailable");
        }
    }
}
