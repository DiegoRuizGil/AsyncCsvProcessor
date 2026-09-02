using System.Text.Json.Serialization;
using AsyncCsvProcessor.Api.Configuration;
using AsyncCsvProcessor.Application;
using AsyncCsvProcessor.Domain;
using AsyncCsvProcessor.Infrastructure;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AsyncCsvProcessorDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddScoped<IAsyncCsvProcessorDbContext>(sp =>
    sp.GetRequiredService<AsyncCsvProcessorDbContext>());

builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));

builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMq:Host"] ?? "localhost", "/", h =>
        {
            h.Username(builder.Configuration["RabbitMq:Username"] ?? "guest");
            h.Password(builder.Configuration["RabbitMq:Password"] ?? "guest");
        });
    });
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AsyncCsvProcessorDbContext>();
    db.Database.Migrate();
}

app.UseSwagger();
app.UseSwaggerUI();

app.MapPost("/jobs", async (
    CreateJobRequest request,
    IAsyncCsvProcessorDbContext db,
    ISendEndpointProvider sendEndpointProvider,
    CancellationToken ct) =>
{
    var job = new Job(request.FileName, request.Priority ?? JobPriority.Normal);
    db.Jobs.Add(job);
    await db.SaveChangesAsync(ct);

    var queue = $"job-submitted-{job.Priority.ToString().ToLowerInvariant()}";
    var sendEndpoint = await sendEndpointProvider.GetSendEndpoint(new Uri($"queue:{queue}"));
    await sendEndpoint.Send(new JobSubmitted(job.Id, job.FileName));
    
    return Results.Created($"/jobs/{job.Id}", new { job.Id, job.Status, job.Priority });
});

app.MapGet("/jobs/{id:guid}", async (Guid id, IAsyncCsvProcessorDbContext db, CancellationToken ct) =>
{
    var job = await db.Jobs.FindAsync([id], ct);
    return job is null ? Results.NotFound() : Results.Ok(job);
});

app.Run();

record CreateJobRequest(string FileName, JobPriority? Priority = null);