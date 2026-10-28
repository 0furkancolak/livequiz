using System.Net;

namespace LiveQuiz.Api.Tests;

// The Orleans silo initialises its Postgres storage at startup, so the Api cannot boot without a database any more.
[Collection(ApiCollection.Name)]
public sealed class HealthTests(ApiFactory factory)
{
    [Fact]
    public async Task Alive_endpoint_returns_ok()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/alive", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
