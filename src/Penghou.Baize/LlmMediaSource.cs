namespace Penghou.Baize;

/// <summary>The transport used to carry non-text content to an endpoint.</summary>
[Flags]
public enum LlmContentTransport
{
    /// <summary>No media transport is supported.</summary>
    None = 0,

    /// <summary>Raw bytes embedded in the request.</summary>
    InlineData = 1,

    /// <summary>An absolute URI dereferenced by the provider.</summary>
    Uri = 2,

    /// <summary>A file previously uploaded to a provider.</summary>
    ProviderFile = 4
}

/// <summary>A provider-neutral source for non-text message content.</summary>
public abstract record LlmMediaSource
{
    /// <summary>The transport this source requires.</summary>
    public abstract LlmContentTransport Transport { get; }
}

/// <summary>Inline media bytes. The constructor takes an immutable snapshot.</summary>
public sealed record LlmInlineDataSource : LlmMediaSource
{
    private readonly byte[] _data;

    /// <summary>Initializes an inline source.</summary>
    public LlmInlineDataSource(ReadOnlyMemory<byte> data)
    {
        if (data.IsEmpty)
            throw new ArgumentException("Inline media data cannot be empty.", nameof(data));

        _data = data.ToArray();
    }

    /// <summary>The immutable media bytes.</summary>
    public ReadOnlyMemory<byte> Data => _data;

    /// <inheritdoc />
    public override LlmContentTransport Transport => LlmContentTransport.InlineData;
}

/// <summary>An absolute URI containing media content.</summary>
public sealed record LlmUriSource : LlmMediaSource
{
    /// <summary>Initializes a URI source.</summary>
    public LlmUriSource(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
            throw new ArgumentException("Media URI must be absolute.", nameof(uri));
        Uri = uri;
    }

    /// <summary>The absolute media URI.</summary>
    public Uri Uri { get; }

    /// <inheritdoc />
    public override LlmContentTransport Transport => LlmContentTransport.Uri;
}

/// <summary>A provider-hosted file reference.</summary>
public sealed record LlmProviderFileSource : LlmMediaSource
{
    /// <summary>Initializes a provider file source.</summary>
    public LlmProviderFileSource(LlmProviderKey provider, string fileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        Provider = provider;
        FileId = fileId;
    }

    /// <summary>The provider that owns the file.</summary>
    public LlmProviderKey Provider { get; }

    /// <summary>The provider-assigned file identifier.</summary>
    public string FileId { get; }

    /// <inheritdoc />
    public override LlmContentTransport Transport => LlmContentTransport.ProviderFile;
}
