using Quartz;

namespace ProxyMov_DownloadServer.Services;

public class QuartzService(ISchedulerFactory schedulerFactory) : IQuartzService
{
    private JobKey? JobKey;

    private string? JobName;

    private IScheduler? Scheduler;
    private ITrigger? Trigger;
    private CancellationTokenSource? CancellationTokenSource { get; set; }
    private CancellationToken CancellationToken { get; set; }

    public async Task Init()
    {
        CancellationTokenSource = new CancellationTokenSource();
        CancellationToken = CancellationTokenSource.Token;
        Scheduler = await schedulerFactory.GetScheduler(CancellationToken);

        JobName = nameof(CronJob);
        JobKey = new JobKey(JobName);
    }

    public async Task CreateJob(int intervalInMinutes)
    {
        if (JobKey != null)
        {
            var job = JobBuilder.Create<CronJob>()
                .WithIdentity(JobKey)
                .Build();

            DateTimeOffset startTime = new DateTimeOffset(DateTime.Now.ToLocalTime())
                .AddSeconds(10);

            Trigger = TriggerBuilder.Create()
                .ForJob(JobKey)
                .WithIdentity(JobName + "-trigger")
                .WithSimpleSchedule(_ =>
                    _.WithIntervalInMinutes(intervalInMinutes)
                        .RepeatForever())
                .StartAt(startTime)
                .Build();

            CronJob.NextRun = startTime.DateTime;
            CronJob.Interval = intervalInMinutes;

            if (Scheduler != null) await Scheduler.ScheduleJob(job, Trigger, CancellationToken);
        }
    }

    public async Task StartJob()
    {
        CancellationTokenSource = new CancellationTokenSource();
        CancellationToken = CancellationTokenSource.Token;

        if (JobKey != null)
        {
            Trigger = TriggerBuilder.Create()
                .ForJob(JobKey)
                .WithIdentity(JobName + "-trigger")
                .WithSimpleSchedule(_ =>
                    _.WithIntervalInMinutes(CronJob.Interval)
                        .RepeatForever())
                .StartNow()
                .Build();
        }

        if (Scheduler != null)
        {
            await Scheduler.RescheduleJob(Trigger.Key, Trigger, CancellationToken);
        }
    }

    public void CancelJob()
    {
        CancellationTokenSource.Cancel();

        if (JobKey != null) Scheduler?.PauseJob(JobKey, CancellationToken);
    }

    public bool IsCancelled()
    {
        return CancellationToken.IsCancellationRequested;
    }
}