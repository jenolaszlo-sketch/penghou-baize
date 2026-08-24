using System;

namespace Penghou.Baize.Tests;

/// <summary>
/// A progress reporter that records synchronously on the caller's flow.
/// <see cref="Progress{T}"/> posts to the thread pool, which races with
/// completion in tests that assert on the recorded sequence.
/// </summary>
internal sealed class SynchronousProgress(Action<double> report) : IProgress<double>
{
    public void Report(double value) => report(value);
}
