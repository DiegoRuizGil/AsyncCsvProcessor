namespace AsyncCsvProcessor.Worker.Configuration;

public class StuckJobRecoveryOptions
{
    public int ThresholdMinutes { get; set; }
    public int CheckIntervalMinutes { get; set; }
    public int MaxAttempts { get; set; }
}