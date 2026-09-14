using System.Net;
using Legendary_Sharp.Downloader.Manifest;

namespace Legendary_Sharp.Downloader;

internal sealed class ChunkSource : IDisposable
{
    private readonly HttpClient _client;
    private readonly string[] _mirrors;
    private readonly int _attempts;

    private long _transferred;
    private int _cursor;

    public ChunkSource(IReadOnlyList<string> mirrors, int connections, TimeSpan timeout, int attempts = 5)
    {
        if (mirrors.Count == 0) throw new ArgumentException("At least one mirror is required.", nameof(mirrors));

        _mirrors = [.. mirrors.Select(mirror => mirror.TrimEnd('/'))];
        _attempts = Math.Max(_mirrors.Length, attempts);

        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = Math.Max(8, connections * 2),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            AutomaticDecompression = DecompressionMethods.None,
            EnableMultipleHttp2Connections = true,
            ConnectTimeout = TimeSpan.FromSeconds(15)
        };

        _client = new HttpClient(handler) { Timeout = timeout };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("LegendarySharp/1.0");
    }

    public long Transferred => Interlocked.Read(ref _transferred);

    public async Task<byte[]> FetchAsync(ChunkInfo chunk, int featureLevel, CancellationToken cancellation)
    {
        var path = chunk.BuildPath(featureLevel);
        var gone = new bool[_mirrors.Length];
        var start = Interlocked.Increment(ref _cursor) - 1;
        Exception? last = null;

        for (var attempt = 0; attempt < _attempts; attempt++)
        {
            if (Array.TrueForAll(gone, value => value)) break;

            var index = (start + attempt) % _mirrors.Length;
            if (gone[index]) continue;

            try
            {
                var payload = await GetAsync($"{_mirrors[index]}/{path}", cancellation).ConfigureAwait(false);
                Interlocked.Add(ref _transferred, payload.Length);
                return ChunkFile.Unpack(payload, chunk, verify: true);
            }
            catch (HttpRequestException error) when (IsGone(error.StatusCode))
            {
                gone[index] = true;
                last = error;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                last = error;
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (1 << Math.Min(attempt, 4))), cancellation)
                    .ConfigureAwait(false);
            }
        }

        if (Array.TrueForAll(gone, value => value))
            throw new ChunkMissingException($"Epic no longer hosts {path}.");

        throw new ChunkDownloadException($"Failed to download {path} after {_attempts} attempts: {last?.Message}", last);
    }

    public async Task<byte[]> GetAsync(string url, CancellationToken cancellation)
    {
        using var response = await _client
            .GetAsync(url, HttpCompletionOption.ResponseContentRead, cancellation)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellation).ConfigureAwait(false);
    }

    public async Task<bool> ExistsAsync(ChunkInfo chunk, int featureLevel, CancellationToken cancellation)
    {
        var path = chunk.BuildPath(featureLevel);

        foreach (var mirror in _mirrors)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, $"{mirror}/{path}");
                using var response = await _client.SendAsync(request, cancellation).ConfigureAwait(false);
                if (response.IsSuccessStatusCode) return true;
            }
            catch (HttpRequestException)
            {
            }
        }

        return false;
    }

    private static bool IsGone(HttpStatusCode? status) =>
        status is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.Gone;

    public void Dispose() => _client.Dispose();
}

internal class ChunkDownloadException(string message, Exception? inner = null) : Exception(message, inner);

internal sealed class ChunkMissingException(string message) : ChunkDownloadException(message);
