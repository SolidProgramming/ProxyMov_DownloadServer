namespace ProxyMov_DownloadServer.Interfaces;

public interface IQuartzService
{
    Task Init();
    Task CreateJob(int intervalInMinutes);
    Task StartJob();
    void CancelJob();
    bool IsCancelled();
}