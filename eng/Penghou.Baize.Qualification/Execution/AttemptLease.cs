using Penghou.Baize.Router;

namespace Penghou.Baize.Qualification.Execution;

// Observed by deterministic tests: how many secret resolutions and lease
// denials a composed subject caused. Never exposes secret values.
internal interface ISecretProbe : ISecretProvider
{
    int SecretReads { get; }
    int LeaseDenials { get; }
}

// The invocation-scoped admission lease. It bridges the legacy adapter path,
// whose neutral HTTP requests carry no HttpRequestContext: the runner owns an
// AsyncLocal lease created during semantic admission and consulted at every
// HTTP admission and secret resolution. A revoked lease denies late or
// background work; it is never a global current-model variable.
internal sealed record AttemptLease(
    string AttemptId,
    string RunId,
    string CaseId,
    string TrialId,
    int TrialSlot,
    string ModelId,
    string ExpectedMethod,
    string ExpectedOrigin,
    string ExpectedBasePath,
    string SecretSlot,
    CancellationToken AttemptToken,
    CancellationToken DeadlineToken,
    DateTimeOffset AbsoluteDeadline,
    TimeSpan IdleTimeout,
    long MaximumRequestBodyBytes,
    long MaximumResponseBodyBytes);

internal static class AttemptLeaseScope
{
    private static readonly AsyncLocal<AttemptLease?> Current = new();

    public static AttemptLease? Active => Current.Value;

    public static IDisposable Enter(AttemptLease lease)
    {
        if (Current.Value is not null) throw new ExecutionException("nested-attempt-lease");
        Current.Value = lease;
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Current.Value = null;
        }
    }
}

// Resolves credentials only while a live attempt lease exists. The wrapper
// rechecks the lease before AND after the inner asynchronous resolution so a
// deferred client can never release a key to a cancelled or revoked waiter.
// Denied lookups never read the underlying provider.
internal sealed class LeaseAwareSecretProvider(ISecretProvider inner) : ISecretProbe
{
    public int SecretReads { get; private set; }
    public int LeaseDenials { get; private set; }

    public async Task<string?> GetSecretAsync(string name, CancellationToken cancellationToken = default)
    {
        var lease = AttemptLeaseScope.Active;
        if (lease is null)
        {
            LeaseDenials++;
            return null;
        }
        if (name != lease.SecretSlot)
        {
            LeaseDenials++;
            return null;
        }
        if (lease.AttemptToken.IsCancellationRequested)
        {
            LeaseDenials++;
            return null;
        }
        var value = await inner.GetSecretAsync(name, cancellationToken).ConfigureAwait(false);
        if (AttemptLeaseScope.Active is null || AttemptLeaseScope.Active.AttemptId != lease.AttemptId)
        {
            LeaseDenials++;
            return null;
        }
        SecretReads++;
        return value;
    }
}

// Deterministic synthetic secret source: only the exact frozen slot resolves,
// with a placeholder value. Never reads the environment.
internal sealed class SyntheticSecretProvider(string slotReference) : ISecretProbe
{
    public const string Placeholder = "synthetic-not-a-credential";
    public int SecretReads { get; private set; }
    public int LeaseDenials { get; private set; }

    public Task<string?> GetSecretAsync(string name, CancellationToken cancellationToken = default)
    {
        if (name != slotReference)
        {
            LeaseDenials++;
            return Task.FromResult<string?>(null);
        }
        SecretReads++;
        return Task.FromResult<string?>(Placeholder);
    }
}
