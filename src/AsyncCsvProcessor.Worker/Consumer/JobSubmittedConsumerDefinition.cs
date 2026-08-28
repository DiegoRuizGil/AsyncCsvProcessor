using MassTransit;

namespace AsyncCsvProcessor.Worker.Consumer;

public class JobSubmittedConsumerDefinition : ConsumerDefinition<JobSubmittedConsumer>
{
    public JobSubmittedConsumerDefinition() { }

    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpointConfigurator, IConsumerConfigurator<JobSubmittedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(r =>
            r.Intervals(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));
    }
}