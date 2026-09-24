using System.Buffers;
using System.Net;
using Legendary_Sharp.Downloader.Manifest;

namespace Legendary_Sharp.Downloader;

internal enum ChunkPresence
{
    Present,
    Gone,
    Unreachable
}

internal sealed class ChunkSource : IDisposable
{
    private static readonly TimeSpan LongestBackoff = TimeSpan.FromSeconds(15);

    private readonly HttpClient _client;
    private readonly string[] _mirrors;
    private readonly int _attempts;
    private readonly TimeSpan _stallTimeout;

    private long _transferred;
    private int _cursor;

    public ChunkSource(IReadOnlyList<string> mirrors, int connections, TimeSpan stallTimeout, int attempts = 10)
    {
        if (mirrors.Count == 0) throw new ArgumentException("At least one mirror is required.", nameof(mirrors));

        _mirrors = [.. mirrors.Select(mirror => mirror.TrimEnd('/'))];
        _attempts = Math.Max(_mirrors.Length, attempts);
        _stallTimeout = stallTimeout;

        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = Math.Max(8, connections * 2),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            AutomaticDecompression = DecompressionMethods.None,
            EnableMultipleHttp2Connections = true,
            ConnectTimeout = TimeSpan.FromSeconds(15)
        };

        _client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("LegendarySharp/1.0");
    }

    public long Transferred => Interlocked.Read(ref _transferred);

    public async Task<byte[]> FetchAsync(ChunkInfo chunk, int featureLevel, CancellationToken cancellation)
    {
        var path = chunk.BuildPath(featureLevel);
        var gone = new bool[_mirrors.Length];
        var start = (int)((uint)Interlocked.Increment(ref _cursor) % (uint)_mirrors.Length);
        var failures = 0;
        Exception? last = null;

        for (var attempt = 0; attempt < _attempts; attempt++)
        {
            if (Array.TrueForAll(gone, value => value)) break;

            var index = (start + attempt) % _mirrors.Length;
            if (gone[index]) continue;

            try
            {
                var payload = await GetAsync($"{_mirrors[index]}/{path}", cancellation).ConfigureAwait(false);
                var data = ChunkFile.Unpack(payload, chunk, verify: true);
                Interlocked.Add(ref _transferred, payload.Length);
                return data;
            }
            catch (HttpRequestException error) when (IsGone(error.StatusCode))
            {
                gone[index] = true;
                last = error;
            }
            catch (Exception error) when (!cancellation.IsCancellationRequested)
            {
                last = error;
                await Task.Delay(Backoff(failures++), cancellation).ConfigureAwait(false);
            }
        }

        if (Array.TrueForAll(gone, value => value))
            throw new ChunkMissingException($"Epic no longer hosts {path}.");

        throw new ChunkDownloadException(
            $"Could not download a chunk after {_attempts} tries, the last error was: {last?.Message}", last);
    }

    public async Task<byte[]> GetAsync(string url, CancellationToken cancellation)
    {
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        stall.CancelAfter(_stallTimeout);

        try
        {
            using var response = await _client
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, stall.Token)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var length = response.Content.Headers.ContentLength;
            await using var body = await response.Content.ReadAsStreamAsync(stall.Token).ConfigureAwait(false);
            using var target = length is > 0 and < int.MaxValue
                ? new MemoryStream((int)length.Value)
                : new MemoryStream();

            var buffer = ArrayPool<byte>.Shared.Rent(1 << 16);

            try
            {
                int read;
                while ((read = await body.ReadAsync(buffer, stall.Token).ConfigureAwait(false)) > 0)
                {
                    target.Write(buffer, 0, read);
                    stall.CancelAfter(_stallTimeout);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            if (length is { } expected && target.Length != expected)
                throw new IOException($"{Host(url)} closed the connection after {target.Length} of {expected} bytes.");

            return target.Length == target.Capacity ? target.GetBuffer() : target.ToArray();
        }
        catch (OperationCanceledException error) when (!cancellation.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"{Host(url)} stopped responding for {_stallTimeout.TotalSeconds:0} seconds.", error);
        }
    }

    public async Task<ChunkPresence> ProbeAsync(ChunkInfo chunk, int featureLevel, CancellationToken cancellation)
    {
        var path = chunk.BuildPath(featureLevel);
        var answered = false;

        foreach (var mirror in _mirrors)
        {
            using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            stall.CancelAfter(_stallTimeout);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, $"{mirror}/{path}");
                using var response = await _client
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token)
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode) return ChunkPresence.Present;
                if (IsGone(response.StatusCode)) answered = true;
            }
            catch (Exception error) when (!cancellation.IsCancellationRequested &&
                                          error is HttpRequestException or OperationCanceledException or IOException)
            {
            }
        }

        return answered ? ChunkPresence.Gone : ChunkPresence.Unreachable;
    }

    private static TimeSpan Backoff(int failures)
    {
        var delay = TimeSpan.FromMilliseconds(500 * Math.Pow(2, Math.Min(failures, 5)));
        return delay < LongestBackoff ? delay : LongestBackoff;
    }

    private static string Host(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;

    private static bool IsGone(HttpStatusCode? status) =>
        status is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.Gone;

    public void Dispose() => _client.Dispose();
}

internal class ChunkDownloadException(string message, Exception? inner = null) : Exception(message, inner);

internal sealed class ChunkMissingException(string message) : ChunkDownloadException(message);
