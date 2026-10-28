using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LiveQuiz.Api.Tests;

public sealed class HealthTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Alive_endpoint_returns_ok_without_touching_the_database()
    {
        // The connection string is a placeholder: /alive only runs the "live"-tagged self check.
        using var client = factory
            .WithWebHostBuilder(b => b
                .UseEnvironment("Development")
                .UseSetting("ConnectionStrings:livequizdb", "Host=localhost;Database=unused")
                .UseSetting("Database:MigrateOnStartup", "false"))
            .CreateClient();

        var response = await client.GetAsync("/alive", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
