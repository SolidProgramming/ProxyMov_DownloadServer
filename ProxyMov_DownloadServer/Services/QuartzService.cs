using Quartz;

namespace ProxyMov_DownloadServer.Services
{
    public class QuartzService(ISchedulerFactory schedulerFactory) : IQuartzService
    {
        private CancellationTokenSource CancellationTokenSource { get; set; }
        public CancellationToken CancellationToken { get; set; }

        public IScheduler? Scheduler;

        private string? JobName;
        private JobKey? JobKey;
        private ITrigger Trigger;

        public async Task Init()
        {
            CancellationTokenSource = new CancellationTokenSource();
            CancellationToken = CancellationTokenSource.Token;
            Scheduler = await schedulerFactory.GetScheduler(CancellationToken);

            JobName = typeof(CronJob).Name;
            JobKey = new(JobName);
        }

        public async Task CreateJob(int intervalInMinutes)
        {
            IJobDetail? job = JobBuilder.Create<CronJob>()
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

            await Scheduler.ScheduleJob(job, Trigger, CancellationToken);
        }

        public async Task StartJob()
        {
            CancellationTokenSource = new CancellationTokenSource();
            CancellationToken = CancellationTokenSource.Token;

            Trigger = TriggerBuilder.Create()
                .ForJob(JobKey)
                .WithIdentity(JobName + "-trigger")
                .WithSimpleSchedule(_ =>
                    _.WithIntervalInMinutes(CronJob.Interval)
                    .RepeatForever())
                .StartNow()
                .Build();

            await Scheduler.RescheduleJob(Trigger.Key, Trigger, CancellationToken);
        }

        public void CancelJob()
        {
            CancellationTokenSource.Cancel();

            Scheduler.PauseJob(JobKey, CancellationToken);
        }

        public bool IsCancelled()
        {
            return CancellationToken.IsCancellationRequested;
        }
    }
}
