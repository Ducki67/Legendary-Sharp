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
    public double Fraction => TotalDownloadBytes > 0 ? (double)DownloadedBytes / TotalDownloadBytes : 0d;
}
