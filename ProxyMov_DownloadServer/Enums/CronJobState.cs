namespace ProxyMov_DownloadServer.Enums;

public enum CronJobState
{
    Undefined,
    WaitForNextCycle,
    CheckingForDownloads,
    Running,
    Paused
}