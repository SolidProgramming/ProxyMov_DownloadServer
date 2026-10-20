namespace ProxyMov_DownloadServer.Classes;

public class DownloadRuntimeState
{
    public delegate void DownloadsChangedEventHandler(int downloadCount, int languageDownloadCount);
    public delegate void ErrorEventHandler(MessageType messageType, string message);
    public delegate void StateChangedEventHandler(CronJobState jobState);
    public delegate void MaxConcurrentDownloadsChangedEventHandler(int maxConcurrentDownloads);

    private readonly Lock _queueLock = new();

    public Queue<EpisodeDownloadModel>? DownloadQueue { get; private set; }
    public List<EpisodeDownloadModel> SkippedDownloads { get; } = [];
    public EpisodeDownloadModel? StopMarkDownload { get; set; }
    public string? PendingCaptchaUrl { get; private set; }
    public int Interval { get; set; }
    public DateTime? NextRun { get; set; }
    public CronJobState CronJobState { get; private set; } = CronJobState.WaitForNextCycle;
    public int DownloadCount { get; private set; }
    public int LanguageDownloadCount { get; private set; }
    public int MaxConcurrentDownloads { get; private set; } = 1;

    public event StateChangedEventHandler? StateChanged;
    public event MaxConcurrentDownloadsChangedEventHandler? MaxConcurrentDownloadsChanged;
    public event ErrorEventHandler? ErrorOccurred;
    public event DownloadsChangedEventHandler? DownloadsChanged;

    public void SetState(CronJobState jobState)
    {
        CronJobState = jobState;
        StateChanged?.Invoke(jobState);
    }

    public void SetDownloadCounts(int downloadCount, int languageDownloadCount)
    {
        DownloadCount = downloadCount;
        LanguageDownloadCount = languageDownloadCount;
        DownloadsChanged?.Invoke(downloadCount, languageDownloadCount);
    }

    public void SetDownloadQueue(Queue<EpisodeDownloadModel>? downloadQueue)
    {
        lock (_queueLock)
        {
            DownloadQueue = downloadQueue;
        }

        DownloadsChanged?.Invoke(DownloadCount, LanguageDownloadCount);
    }

    public bool TryDequeueDownload(out EpisodeDownloadModel? download, out int remainingCount)
    {
        lock (_queueLock)
        {
            download = null;
            remainingCount = DownloadQueue?.Count ?? 0;

            if (DownloadQueue is null || !DownloadQueue.TryDequeue(out download)) return false;

            remainingCount = DownloadQueue.Count;
            return true;
        }
    }

    public int GetQueuedDownloadCount()
    {
        lock (_queueLock)
        {
            return DownloadQueue?.Count ?? 0;
        }
    }

    public void SetMaxConcurrentDownloads(int maxConcurrentDownloads)
    {
        maxConcurrentDownloads = Math.Clamp(maxConcurrentDownloads, 1, SettingsModel.MaxConcurrentDownloadsLimit);

        if (MaxConcurrentDownloads == maxConcurrentDownloads) return;

        MaxConcurrentDownloads = maxConcurrentDownloads;
        MaxConcurrentDownloadsChanged?.Invoke(maxConcurrentDownloads);
    }

    public void ClearSkippedDownloads()
    {
        SkippedDownloads.Clear();
    }

    public void AddSkippedDownload(EpisodeDownloadModel download)
    {
        if (!SkippedDownloads.Contains(download))
        {
            SkippedDownloads.Add(download);
        }
    }

    public List<EpisodeDownloadModel>? GetVisibleDownloads()
    {
        lock (_queueLock)
        {
            return DownloadQueue?.ToList().Except(SkippedDownloads).ToList();
        }
    }

    public void RaiseError(MessageType messageType, string message)
    {
        ErrorOccurred?.Invoke(messageType, message);
    }

    public void SetPendingCaptchaUrl(string? url)
    {
        PendingCaptchaUrl = url;
    }
}
