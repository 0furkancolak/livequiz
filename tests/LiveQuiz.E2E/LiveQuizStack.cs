using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Testcontainers.PostgreSql;

namespace LiveQuiz.E2E;

/// <summary>
/// The real thing, minus the orchestrator: a PostgreSQL container plus the Api and Web processes started from the
/// build output, and a Chromium to drive them. One stack serves every test; tests isolate themselves with unique users.
/// </summary>
public sealed class LiveQuizStack : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17.11-alpine").Build();
    private readonly List<Process> _processes = [];
    private readonly StringBuilder _log = new();
    private IPlaywright? _playwright;

    public IBrowser Browser { get; private set; } = null!;
    public string BaseUrl { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        // LIVEQUIZ_BASE_URL points the tests at a stack that is already running (docker compose, the AppHost).
        if (Environment.GetEnvironmentVariable("LIVEQUIZ_BASE_URL") is { Length: > 0 } running)
        {
            BaseUrl = running.TrimEnd('/');
            await LaunchBrowserAsync();
            return;
        }

        var postgresStarted = _postgres.StartAsync();

        var apiPort = FreePort();
        var web = FreePort();
        var silo = FreePort();
        var gateway = FreePort();
        BaseUrl = $"http://127.0.0.1:{web}";

        await postgresStarted;
        var connection = _postgres.GetConnectionString();
        var keys = Directory.CreateTempSubdirectory("livequiz-e2e-keys").FullName;

        Start("LiveQuiz.Api", new Dictionary<string, string>
        {
            ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{apiPort}",
            ["ConnectionStrings__livequizdb"] = connection,
            ["Database__MigrateOnStartup"] = "true",
            // Own ports, so a development AppHost running on the same machine does not collide with the tests.
            ["Cluster__Provider"] = "AdoNet",
            ["Cluster__SiloPort"] = silo.ToString(),
            ["Cluster__GatewayPort"] = gateway.ToString(),
            ["DataProtection__KeysPath"] = keys
        });
        Start("LiveQuiz.Web", new Dictionary<string, string>
        {
            ["ASPNETCORE_URLS"] = BaseUrl,
            ["services__api__http__0"] = $"http://127.0.0.1:{apiPort}",
            ["DataProtection__KeysPath"] = keys
        });

        await WaitForHealthyAsync($"http://127.0.0.1:{apiPort}/health");
        await WaitForHealthyAsync($"{BaseUrl}/health");

        await LaunchBrowserAsync();
    }

    private async Task LaunchBrowserAsync()
    {
        // Idempotent: a no-op when the browser is already installed.
        // On a CI runner the system libraries Chromium needs are installed too.
        var install = Microsoft.Playwright.Program.Main(
            Environment.GetEnvironmentVariable("CI") == "true" ? ["install", "--with-deps", "chromium"] : ["install", "chromium"]);
        if (install != 0)
            throw new InvalidOperationException("playwright install chromium failed.");
        _playwright = await Playwright.CreateAsync();
        // Cold paths (first WebAssembly start, first call through the proxy) are slow; assertions retry until then.
        Assertions.SetDefaultExpectTimeout(20_000);
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            // Slows every action down so a recorded run is readable (docs/demo.md).
            SlowMo = float.TryParse(Environment.GetEnvironmentVariable("LIVEQUIZ_E2E_SLOWMO"), out var slowMo) ? slowMo : null
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (Browser is not null)
            await Browser.CloseAsync();
        _playwright?.Dispose();
        foreach (var process in _processes)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            catch (InvalidOperationException)
            {
            }
        }

        await _postgres.DisposeAsync(); // never started when the tests run against an existing stack: a no-op
    }

    /// <summary>What the Api and Web processes have written so far; attach to a failure to see why.</summary>
    public string ProcessLog
    {
        get
        {
            lock (_log)
                return _log.ToString();
        }
    }

    private void Start(string project, Dictionary<string, string> environment)
    {
        var root = RepositoryRoot();
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name; // tests/LiveQuiz.E2E/bin/<Configuration>/net10.0
        var output = Path.Combine(root, "src", project, "bin", configuration, "net10.0");

        var start = new ProcessStartInfo("dotnet", $"\"{Path.Combine(output, project + ".dll")}\"")
        {
            WorkingDirectory = Path.Combine(root, "src", project), // content root, like `dotnet run`
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        foreach (var (key, value) in environment)
            start.Environment[key] = value;

        var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {project}.");
        void Capture(object _, DataReceivedEventArgs e)
        {
            lock (_log)
                _log.AppendLine($"[{project}] {e.Data}");
        }

        process.OutputDataReceived += Capture;
        process.ErrorDataReceived += Capture;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _processes.Add(process);
    }

    private async Task WaitForHealthyAsync(string url)
    {
        using var http = new HttpClient();
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            if (_processes.Any(p => p.HasExited))
                throw new InvalidOperationException($"A process exited during startup.\n{ProcessLog}");
            try
            {
                if ((await http.GetAsync(url)).IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"{url} did not become healthy.\n{ProcessLog}");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LiveQuiz.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("LiveQuiz.sln not found above the test output.");
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

[CollectionDefinition(Name)]
public sealed class StackCollection : ICollectionFixture<LiveQuizStack>
{
    public const string Name = "stack";
}
