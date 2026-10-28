var builder = DistributedApplication.CreateBuilder(args);

// Postgres data lives in a bind mount inside the repo (.data/, git-ignored) so it survives
// restarts without growing the Docker VM disk.
var dataDir = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", ".data", "postgres"));
Directory.CreateDirectory(dataDir);

// Pinned to PostgreSQL 17 (alpine): small image, and matches what Testcontainers will use in CI.
// Api and Web must share Data Protection keys so the Identity cookie issued by one is valid in the other.
var keysDir = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", ".data", "keys"));
Directory.CreateDirectory(keysDir);

var postgres = builder.AddPostgres("postgres")
    .WithImageTag("17.11-alpine")
    .WithDataBindMount(dataDir);
var db = postgres.AddDatabase("livequizdb");

// Several Api instances, each with its own Orleans silo, form one cluster through the Postgres membership table
// (ADR 0016). Aspire cannot give replicas of one resource their own silo ports, so each instance is a resource.
var instanceCount = int.TryParse(builder.Configuration["Api:Instances"], out var n) ? n : 2;
var apis = new List<IResourceBuilder<ProjectResource>>();
for (var i = 1; i <= instanceCount; i++)
{
    var api = builder.AddProject<Projects.LiveQuiz_Api>($"api-{i}", project => project.ExcludeLaunchProfile = true)
        .WithHttpEndpoint(port: 5196 + i, name: "http", isProxied: false) // no proxy: a dead instance must refuse connections, not hold them
        .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
        .WithReference(db)
        .WithEnvironment("Cluster__Provider", "AdoNet")
        .WithEnvironment("Cluster__SiloPort", (11110 + i).ToString())
        .WithEnvironment("Cluster__GatewayPort", (30000 + i).ToString())
        .WithEnvironment("DataProtection__KeysPath", keysDir)
        .WaitFor(db)
        .WithHttpHealthCheck("/health");
    apis.Add(api);
}

var web = builder.AddProject<Projects.LiveQuiz_Web>("web")
    .WithEnvironment("DataProtection__KeysPath", keysDir)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health");

// Web reaches the instances through service discovery as api-1, api-2, ... and spreads requests across them.
web.WithEnvironment("Api__Instances", string.Join(',', apis.Select(a => a.Resource.Name)));
foreach (var api in apis)
    web.WithReference(api).WaitFor(api);

builder.Build().Run();
