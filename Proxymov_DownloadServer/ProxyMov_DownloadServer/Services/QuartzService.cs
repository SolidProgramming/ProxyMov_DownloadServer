using Quartz;

namespace ProxyMov_DownloadServer.Services;

public class QuartzService(ISchedulerFactory schedulerFactory, DownloadRuntimeState runtimeState) : IQuartzService
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
            IJobDetail job = JobBuilder.Create<CronJob>()
                .WithIdentity(JobKey)
                .Build();

            DateTimeOffset startTime = new DateTimeOffset(DateTime.UtcNow.ToLocalTime())
                .AddSeconds(10);

            Trigger = TriggerBuilder.Create()
                .ForJob(JobKey)
                .WithIdentity(JobName + "-trigger")
                .WithSimpleSchedule(_ =>
                    _.WithIntervalInMinutes(intervalInMinutes)
                        .RepeatForever())
                .StartAt(startTime)
                .Build();

            runtimeState.NextRun = startTime.DateTime;
            runtimeState.Interval = intervalInMinutes;

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
                    _.WithIntervalInMinutes(runtimeState.Interval)
                        .RepeatForever())
                .StartNow()
                .Build();
        }

        if (Scheduler != null && Trigger is not null)
        {
            await Scheduler.RescheduleJob(Trigger.Key, Trigger, CancellationToken);
        }
    }

    public void CancelJob()
    {
        CancellationTokenSource?.Cancel();

        if (JobKey != null) Scheduler?.PauseJob(JobKey, CancellationToken);
    }

    public bool IsCancelled()
    {
        return CancellationToken.IsCancellationRequested;
    }
}
