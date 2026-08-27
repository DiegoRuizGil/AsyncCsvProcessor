using AsyncCsvProcessor.Application;
using AsyncCsvProcessor.Domain;
using AsyncCsvProcessor.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AsyncCsvProcessorDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddScoped<IAsyncCsvProcessorDbContext>(sp =>
    sp.GetRequiredService<AsyncCsvProcessorDbContext>());

var app = builder.Build();

app.MapPost("/jobs", async (CreateJobRequest request, IAsyncCsvProcessorDbContext db, CancellationToken ct) =>
{
    var job = new Job(request.FileName);
    db.Jobs.Add(job);
    await db.SaveChangesAsync(ct);
    return Results.Created($"/jobs/{job.Id}", new { job.Id, job.Status });
});

app.MapGet("/jobs/{id:guid}", async (Guid id, IAsyncCsvProcessorDbContext db, CancellationToken ct) =>
{
    var job = await db.Jobs.FindAsync([id], ct);
    return job is null ? Results.NotFound() : Results.Ok(job);
});

app.Run();

record CreateJobRequest(string FileName);