using System.Text;

namespace Penghou.Baize;

internal sealed record StreamFramedSegment(
    string Value,
    bool IsProtocolMarker);

/// <summary>
/// Retains only a suffix that can still become a configured protocol marker.
/// Everything else is released verbatim as soon as it is known to be text.
/// </summary>
internal sealed class StreamMarkerLookahead
{
    private readonly string[] _markers;
    private readonly StringBuilder _buffer = new();

    public StreamMarkerLookahead(IEnumerable<string> markers)
    {
        ArgumentNullException.ThrowIfNull(markers);
        _markers = markers
            .Select(marker =>
            {
                ArgumentException.ThrowIfNullOrEmpty(marker);
                return marker;
            })
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(marker => marker.Length)
            .ThenBy(marker => marker, StringComparer.Ordinal)
            .ToArray();

        for (var index = 0; index < _markers.Length; index++)
        {
            for (var other = index + 1; other < _markers.Length; other++)
            {
                if (_markers[index].StartsWith(
                        _markers[other],
                        StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "Protocol markers must be prefix-free.",
                        nameof(markers));
                }
            }
        }
    }

    public int BufferedCharacterCount => _buffer.Length;

    public IReadOnlyList<StreamFramedSegment> Append(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0)
            return [];

        _buffer.Append(value);
        return ReleaseAvailableSegments();
    }

    public IReadOnlyList<StreamFramedSegment> Complete()
    {
        if (_buffer.Length == 0)
            return [];

        var value = _buffer.ToString();
        _buffer.Clear();
        return [new StreamFramedSegment(value, IsProtocolMarker: false)];
    }

    private IReadOnlyList<StreamFramedSegment> ReleaseAvailableSegments()
    {
        var result = new List<StreamFramedSegment>();
        while (_buffer.Length > 0)
        {
            var text = _buffer.ToString();
            var marker = FindFirstMarker(text);
            if (marker is not null)
            {
                if (marker.Value.Index > 0)
                {
                    result.Add(new(
                        text[..marker.Value.Index],
                        IsProtocolMarker: false));
                }

                result.Add(new(
                    marker.Value.Marker,
                    IsProtocolMarker: true));
                _buffer.Remove(
                    0,
                    marker.Value.Index + marker.Value.Marker.Length);
                continue;
            }

            var retained = LongestMarkerPrefixSuffix(text);
            var released = text.Length - retained;
            if (released > 0)
            {
                result.Add(new(
                    text[..released],
                    IsProtocolMarker: false));
                _buffer.Remove(0, released);
            }

            break;
        }

        return result;
    }

    private (int Index, string Marker)? FindFirstMarker(string value)
    {
        (int Index, string Marker)? result = null;
        foreach (var marker in _markers)
        {
            var index = value.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0)
                continue;

            if (result is null ||
                index < result.Value.Index ||
                index == result.Value.Index &&
                marker.Length > result.Value.Marker.Length)
            {
                result = (index, marker);
            }
        }

        return result;
    }

    private int LongestMarkerPrefixSuffix(string value)
    {
        var maximum = 0;
        foreach (var marker in _markers)
        {
            var candidateMaximum = Math.Min(
                value.Length,
                marker.Length - 1);
            for (var length = candidateMaximum;
                 length > maximum;
                 length--)
            {
                if (value.AsSpan(value.Length - length)
                    .SequenceEqual(marker.AsSpan(0, length)))
                {
                    maximum = length;
                    break;
                }
            }
        }

        return maximum;
    }
}
