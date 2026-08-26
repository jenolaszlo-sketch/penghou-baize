using System.Threading;

namespace Penghou.Baize;

internal sealed class StreamBoundaryContext
{
    private static readonly AsyncLocal<StreamBoundaryContext?> Current = new();
    private int _providerChunkCount;
    private int _providerCharacterCount;

    public int ProviderChunkCount => _providerChunkCount;
    public int ProviderCharacterCount => _providerCharacterCount;

    public static IDisposable Push(StreamBoundaryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var previous = Current.Value;
        Current.Value = context;
        return new Scope(previous);
    }

    public static void RecordProviderChunk(int characterCount)
    {
        if (characterCount < 0)
            throw new ArgumentOutOfRangeException(nameof(characterCount));

        var current = Current.Value;
        if (current is null)
            return;

        checked
        {
            current._providerChunkCount++;
            current._providerCharacterCount += characterCount;
        }
    }

    private sealed class Scope(StreamBoundaryContext? previous) : IDisposable
    {
        private StreamBoundaryContext? _previous = previous;
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            Current.Value = _previous;
            _previous = null;
            _disposed = true;
        }
    }
}
