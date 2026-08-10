namespace Penghou.Baize;

/// <summary>Validates provider-neutral batch requests and handles.</summary>
public static class BatchRequestValidator
{
    /// <summary>Validates item identifiers before a provider submission.</summary>
    public static void ValidateItems(
        IReadOnlyList<BaizeBatchItem> items,
        string providerId)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        if (items.Count == 0)
        {
            throw new ArgumentException(
                $"A {providerId} batch requires at least one request.",
                nameof(items));
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.RequestId))
            {
                throw new ArgumentException(
                    "Every batch item must have a non-empty request id.",
                    nameof(items));
            }

            if (!ids.Add(item.RequestId))
            {
                throw new ArgumentException(
                    $"Duplicate batch request id '{item.RequestId}'.",
                    nameof(items));
            }
        }
    }

    /// <summary>Validates that a serialized handle belongs to a provider client.</summary>
    public static void ValidateHandle(
        ProviderBatchHandle handle,
        string providerId)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        if (!string.Equals(
                handle.ProviderId,
                providerId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Batch handle belongs to provider '{handle.ProviderId}', not '{providerId}'.",
                nameof(handle));
        }

        if (string.IsNullOrWhiteSpace(handle.BatchId))
        {
            throw new ArgumentException(
                "Batch handle must contain a provider batch id.",
                nameof(handle));
        }
    }
}
