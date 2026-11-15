namespace Legendary_Sharp.Downloader;

internal readonly record struct DownloadProgress(
    long DownloadedBytes,
    long TotalDownloadBytes,
    long WrittenBytes,
    long TotalWriteBytes,
    int FilesDone,
    int FilesTotal,
    int ChunksDone,
    int ChunksTotal,
    int ActiveWorkers,
    int CachedChunks,
    double BytesPerSecond,
    double WriteBytesPerSecond,
    TimeSpan Elapsed,
    TimeSpan Eta,
    string CurrentFile,
    int WorkerLimit = 0,
    string Easing = "")
{
    public const long WorkPerFile = 256L << 10;

    public long WorkDone => WrittenBytes + FilesDone * WorkPerFile;

    public long WorkTotal => TotalWriteBytes + FilesTotal * WorkPerFile;

    public bool IsWriting => TotalDownloadBytes > 0 && DownloadedBytes >= TotalDownloadBytes && FilesDone < FilesTotal;

    public double Fraction
    {
        get
        {
            var downloaded = TotalDownloadBytes > 0 ? (double)DownloadedBytes / TotalDownloadBytes : 1d;
            var written = WorkTotal > 0 ? (double)WorkDone / WorkTotal : 1d;
            return Math.Clamp(Math.Min(downloaded, written), 0d, 1d);
        }
    }

    public string Activity => IsWriting ? "writing files" : "downloading";
}
