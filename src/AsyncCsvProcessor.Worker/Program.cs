using AsyncCsvProcessor.Application;
using AsyncCsvProcessor.Infrastructure;
using AsyncCsvProcessor.Worker.Configuration;
using AsyncCsvProcessor.Worker.Consumer;
using AsyncCsvProcessor.Worker.Csv;
using AsyncCsvProcessor.Worker.Scheduling;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quartz;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<AsyncCsvProcessorDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddScoped<IAsyncCsvProcessorDbContext>(sp =>
    sp.GetRequiredService<AsyncCsvProcessorDbContext>());

builder.Services.Configure<StuckJobRecoveryOptions>(
    builder.Configuration.GetSection("StuckJobRecovery"));

builder.Services.AddScoped<IJobFileProcessor, CsvJobFileProcessor>();

builder.Services.AddQuartz();
builder.Services.AddQuartzHostedService(options =>
{
    options.WaitForJobsToComplete = true;
});

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<JobSubmittedConsumer, JobSubmittedConsumerDefinition>();
    x.AddConsumer<JobSubmittedFaultConsumer>();
    x.AddConsumer<StuckJobsCheckConsumer>();
    
    x.AddPublishMessageScheduler();
    x.AddQuartzConsumers();
    
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMq:Host"] ?? "localhost", "/", h =>
        {
            h.Username(builder.Configuration["RabbitMq:Username"] ?? "guest");
            h.Password(builder.Configuration["RabbitMq:Password"] ?? "guest");
        });
        
        cfg.UsePublishMessageScheduler();
        
        cfg.ReceiveEndpoint("job-submitted-high", e =>
        {
            e.ConcurrentMessageLimit = 10;
            e.ConfigureConsumer<JobSubmittedConsumer>(context);
        });
        cfg.ReceiveEndpoint("job-submitted-normal", e =>
        {
            e.ConcurrentMessageLimit = 5;
            e.ConfigureConsumer<JobSubmittedConsumer>(context);
        });
        cfg.ReceiveEndpoint("job-submitted-low", e =>
        {
            e.ConcurrentMessageLimit = 2;
            e.ConfigureConsumer<JobSubmittedConsumer>(context);
        });
        cfg.ReceiveEndpoint("check-stuck-jobs", e =>
        {
            e.ConfigureConsumer<JobSubmittedConsumer>(context);
        });
        
        cfg.ConfigureEndpoints(context);
    });
});

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var recurringScheduler = scope.ServiceProvider.GetRequiredService<IRecurringMessageScheduler>();
    var recoveryOptions = scope.ServiceProvider.GetRequiredService<IOptions<StuckJobRecoveryOptions>>().Value;

    await recurringScheduler.ScheduleRecurringSend(
        new Uri("queue:check-stuck-jobs"),
        new CheckStuckJobsSchedule(recoveryOptions.CheckIntervalMinutes),
        new CheckStuckJobs());
}

await host.RunAsync();
