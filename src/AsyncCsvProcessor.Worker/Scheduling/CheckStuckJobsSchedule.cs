using MassTransit.Scheduling;

namespace AsyncCsvProcessor.Worker.Scheduling;

public class CheckStuckJobsSchedule : DefaultRecurringSchedule
{
    public CheckStuckJobsSchedule(int intervalMinutes)
    {
        ScheduleId = "check-stuck-jobs";
        CronExpression = $"0 0/{intervalMinutes} * * * ?";
    }
}