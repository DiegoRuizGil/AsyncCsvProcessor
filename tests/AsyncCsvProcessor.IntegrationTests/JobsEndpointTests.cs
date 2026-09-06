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
}