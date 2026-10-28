using LiveQuiz.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ServiceDiscovery;

namespace LiveQuiz.Web.Tests;

public sealed class ApiInstanceHandlerTests
{
    private static ApiInstanceHandler Handler(Dictionary<string, string?> settings, RecordingHandler inner)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var provider = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddLogging()
            .AddServiceDiscovery()
            .BuildServiceProvider();
        return new ApiInstanceHandler(new ApiInstances(configuration), provider.GetRequiredService<ServiceEndpointResolver>())
        {
            InnerHandler = inner
        };
    }

    [Fact]
    public async Task Requests_are_sent_to_the_address_configured_for_each_instance_in_turn()
    {
        var inner = new RecordingHandler();
        var handler = Handler(new()
        {
            ["Api:Instances"] = "api-1,api-2",
            ["services:api-1:http:0"] = "http://api-1:8080",
            ["services:api-2:http:0"] = "http://api-2:9090"
        }, inner);
        using var client = new HttpClient(handler);

        await client.GetAsync("https+http://api/api/quizzes", TestContext.Current.CancellationToken);
        await client.GetAsync("https+http://api/api/quizzes", TestContext.Current.CancellationToken);

        Assert.Equal(["http://api-1:8080/api/quizzes", "http://api-2:9090/api/quizzes"], inner.Requests);
    }

    [Fact]
    public async Task An_unreachable_instance_is_skipped_for_the_next_one()
    {
        var inner = new RecordingHandler { Fail = uri => uri.Host == "api-1" };
        var handler = Handler(new()
        {
            ["Api:Instances"] = "api-1,api-2",
            ["services:api-1:http:0"] = "http://api-1:8080",
            ["services:api-2:http:0"] = "http://api-2:8080"
        }, inner);
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("https+http://api/api/quizzes", TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal("api-2", inner.Requests.Last().Split('/')[2].Split(':')[0]);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public Func<Uri, bool> Fail { get; set; } = _ => false;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            if (Fail(request.RequestUri))
                throw new HttpRequestException("Connection refused");
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
