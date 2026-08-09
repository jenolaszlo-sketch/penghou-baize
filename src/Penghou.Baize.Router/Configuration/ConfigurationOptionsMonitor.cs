using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Penghou.Baize.Router.Configuration;

/// <summary>
/// Self-contained <see cref="IOptionsMonitor{T}"/> that re-binds a
/// configuration section on reload and only publishes changes that pass the
/// validator, keeping the last known-good value otherwise.
/// </summary>
/// <typeparam name="T">The options type to bind.</typeparam>
internal sealed class ConfigurationOptionsMonitor<T> : IOptionsMonitor<T>, IDisposable
    where T : class, new()
{
    private readonly IConfigurationSection _section;
    private readonly Func<T, string?, bool> _validator;
    private readonly object _gate = new();
    private readonly List<Action<T, string?>> _listeners = [];
    private T _currentValue;
    private IDisposable? _reloadRegistration;
    private bool _subscribed;

    /// <summary>Initializes a monitor from a configuration section.</summary>
    /// <param name="section">The section to bind options from.</param>
    /// <param name="validator">Validates a newly bound value before it is published; invalid values are discarded.</param>
    public ConfigurationOptionsMonitor(
        IConfigurationSection section,
        Func<T, string?, bool>? validator = null)
    {
        _section = section;
        _validator = validator ?? ((_, _) => true);
        _currentValue = section.Get<T>() ?? new T();
    }

    /// <inheritdoc />
    public T CurrentValue
    {
        get
        {
            lock (_gate)
            {
                return _currentValue;
            }
        }
    }

    /// <inheritdoc />
    public T Get(string? name) => CurrentValue;

    /// <inheritdoc />
    public IDisposable OnChange(Action<T, string?> listener)
    {
        lock (_gate)
        {
            _listeners.Add(listener);
            EnsureSubscribedLocked();

            return new Registration(() =>
            {
                lock (_gate)
                {
                    _listeners.Remove(listener);

                    if (_listeners.Count == 0)
                    {
                        _reloadRegistration?.Dispose();
                        _reloadRegistration = null;
                        _subscribed = false;
                    }
                }
            });
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _reloadRegistration?.Dispose();
            _reloadRegistration = null;
            _subscribed = false;
            _listeners.Clear();
        }
    }

    private void EnsureSubscribedLocked()
    {
        if (_subscribed)
            return;

        _reloadRegistration = ChangeToken.OnChange(() => _section.GetReloadToken(), ReloadAll);
        _subscribed = true;
    }

    private void ReloadAll()
    {
        T? next;
        List<Action<T, string?>>? listeners;

        lock (_gate)
        {
            next = _section.Get<T>();
            if (next is null)
                return;

            if (!_validator(next, null))
                return;

            _currentValue = next;
            listeners = [.. _listeners];
        }

        foreach (var listener in listeners)
            listener(next, Options.DefaultName);
    }

    private sealed class Registration(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
