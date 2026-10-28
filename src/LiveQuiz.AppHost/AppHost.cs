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

var api = builder.AddProject<Projects.LiveQuiz_Api>("api")
    .WithReference(db)
    .WithEnvironment("DataProtection__KeysPath", keysDir)
    .WaitFor(db)
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.LiveQuiz_Web>("web")
    .WithReference(api)
    .WithEnvironment("DataProtection__KeysPath", keysDir)
    .WaitFor(api)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health");

builder.Build().Run();
