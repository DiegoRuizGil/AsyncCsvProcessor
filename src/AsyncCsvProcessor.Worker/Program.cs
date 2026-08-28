using AsyncCsvProcessor.Application;
using AsyncCsvProcessor.Infrastructure;
using AsyncCsvProcessor.Worker.Consumer;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<AsyncCsvProcessorDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddScoped<IAsyncCsvProcessorDbContext>(sp =>
    sp.GetRequiredService<AsyncCsvProcessorDbContext>());

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<JobSubmittedConsumer, JobSubmittedConsumerDefinition>();
    
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMq:Host"] ?? "localhost", "/", h =>
        {
            h.Username(builder.Configuration["RabbitMq:Username"] ?? "guest");
            h.Password(builder.Configuration["RabbitMq:Password"] ?? "guest");
        });
        
        cfg.ConfigureEndpoints(context);
    });
});

var host = builder.Build();
host.Run();
