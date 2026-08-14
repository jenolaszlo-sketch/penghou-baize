namespace Penghou.Baize.Generation;

/// <summary>The transport of a generated asset.</summary>
public abstract record GeneratedAssetSource;

/// <summary>A generated asset referenced by a temporary or permanent URL.</summary>
public sealed record UriGeneratedAssetSource : GeneratedAssetSource
{
    /// <summary>Initializes a URI asset source.</summary>
    /// <param name="uri">The generated asset URL.</param>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="uri"/> is not absolute.</exception>
    public UriGeneratedAssetSource(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
            throw new ArgumentException("Generated asset URI must be absolute.", nameof(uri));
        Uri = uri;
    }

    /// <summary>The generated asset URL.</summary>
    public Uri Uri { get; }
}

/// <summary>A generated asset delivered inline as bytes.</summary>
public sealed record InlineGeneratedAssetSource : GeneratedAssetSource
{
    private readonly byte[] _data;

    /// <summary>Initializes an inline asset source.</summary>
    /// <param name="data">The generated asset bytes.</param>
    /// <param name="contentType">The asset content type, when known.</param>
    /// <exception cref="ArgumentException"><paramref name="data"/> is empty.</exception>
    public InlineGeneratedAssetSource(
        ReadOnlyMemory<byte> data,
        string? contentType = null)
    {
        if (data.IsEmpty)
            throw new ArgumentException("Inline generated asset data cannot be empty.", nameof(data));
        _data = data.ToArray();
        ContentType = contentType;
    }

    /// <summary>The immutable generated asset bytes.</summary>
    public ReadOnlyMemory<byte> Data => _data;

    /// <summary>The generated asset content type, when known.</summary>
    public string? ContentType { get; }
}

/// <summary>A generated asset persisted as a provider-hosted file.</summary>
public sealed record ProviderGeneratedAssetSource : GeneratedAssetSource
{
    /// <summary>Initializes a provider-file asset source.</summary>
    /// <param name="providerFileId">The provider-assigned asset file identifier.</param>
    /// <param name="provider">The provider that owns the file, when known.</param>
    /// <exception cref="ArgumentException"><paramref name="providerFileId"/> is empty.</exception>
    public ProviderGeneratedAssetSource(
        string providerFileId,
        string? provider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerFileId);
        ProviderFileId = providerFileId;
        Provider = provider;
    }

    /// <summary>The provider-assigned asset file identifier.</summary>
    public string ProviderFileId { get; }

    /// <summary>The provider that owns the file, when known.</summary>
    public string? Provider { get; }
}