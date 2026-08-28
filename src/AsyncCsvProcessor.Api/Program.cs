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

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapPost("/jobs", async (
    CreateJobRequest request,
    IAsyncCsvProcessorDbContext db,
    IPublishEndpoint publishEndpoint,
    CancellationToken ct) =>
{
    var job = new Job(request.FileName);
    db.Jobs.Add(job);
    await db.SaveChangesAsync(ct);

    await publishEndpoint.Publish(new JobSubmitted(job.Id, job.FileName), ct);
    
    return Results.Created($"/jobs/{job.Id}", new { job.Id, job.Status });
});

app.MapGet("/jobs/{id:guid}", async (Guid id, IAsyncCsvProcessorDbContext db, CancellationToken ct) =>
{
    var job = await db.Jobs.FindAsync([id], ct);
    return job is null ? Results.NotFound() : Results.Ok(job);
});

app.Run();

record CreateJobRequest(string FileName);