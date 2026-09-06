using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AsyncCsvProcessor.Application;
using AsyncCsvProcessor.Domain;
using AsyncCsvProcessor.IntegrationTests.Fixtures;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AsyncCsvProcessor.IntegrationTests;

[Collection("Postgres collection")]
public class JobsEndpointTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly PostgresContainerFixture _dbFixture;
    private readonly CustomWebApplicationFactory _factory;

    public JobsEndpointTests(PostgresContainerFixture dbFixture, CustomWebApplicationFactory factory)
    {
        _dbFixture = dbFixture;
        _factory = factory;
    }

    public Task InitializeAsync() => _dbFixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PostJobs_creates_job_stores_file_and_sends_job_submitted_message()
    {
        var client = _factory.CreateClient();

        using var content = new MultipartFormDataContent();
        var csvBytes = "Sku,Name,Price,Category,Stock\nSKU-1,Mechanical keyboard,29.99,Peripheral,10\n"u8.ToArray();
        var fileContent = new ByteArrayContent(csvBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(fileContent, "File", "products.csv");
        content.Add(new StringContent("High"), "Priority");

        var response = await client.PostAsync("/jobs", content);
        
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var responseBody = await response.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = responseBody.GetProperty("id").GetGuid();
        
        Assert.Equal("High", responseBody.GetProperty("priority").GetString());

        await using var assertDb = _dbFixture.CreateDbContext();
        var persistedJob = await assertDb.Jobs.SingleAsync(j => j.Id == jobId);
        Assert.Equal(JobStatus.Pending, persistedJob.Status);
        Assert.Equal("products.csv", persistedJob.FileName);
        Assert.Equal(JobPriority.High, persistedJob.Priority);
        
        Assert.True(File.Exists(persistedJob.FilePath));
        Assert.StartsWith(_factory.UploadsPath, persistedJob.FilePath);

        var harness = _factory.Services.GetRequiredService<ITestHarness>();
        Assert.True(await harness.Sent.Any<JobSubmitted>(message => message.Context.Message.JobId == jobId));
    }
    
    [Fact]
    public async Task PostJobs_returns_bad_request_when_file_is_empty()
    {
        var client = _factory.CreateClient();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent([]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(fileContent, "File", "empty.csv");

        var response = await client.PostAsync("/jobs", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetJob_returns_ok_with_job_data_when_job_exists()
    {
        var job = new Job("products.csv", "tmp/products.csv", JobPriority.Low);
        await using (var seedDb = _dbFixture.CreateDbContext())
        {
            seedDb.Jobs.Add(job);
            await seedDb.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/jobs/{job.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(job.Id, body.GetProperty("id").GetGuid());
        Assert.Equal("products.csv", body.GetProperty("fileName").GetString());
        Assert.Equal("Pending", body.GetProperty("status").GetString());
        Assert.Equal("Low", body.GetProperty("priority").GetString());
    }

    [Fact]
    public async Task GetJob_returns_not_found_when_job_does_not_exist()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/jobs/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}