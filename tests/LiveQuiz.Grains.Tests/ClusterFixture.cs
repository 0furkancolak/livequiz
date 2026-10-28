using LiveQuiz.Grains.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Orleans.Hosting;
using Orleans.TestingHost;

namespace LiveQuiz.Grains.Tests;

public sealed class ClusterFixture : IAsyncLifetime
{
    public TestCluster Cluster { get; } = new TestClusterBuilder(1)
        .AddSiloBuilderConfigurator<SiloConfigurator>()
        .Build();

    /// <summary>Shared fake clock: grains read time and arm timers through it, so tests control deadlines exactly.</summary>
    public static FakeTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);

    public IGrainFactory Grains => Cluster.GrainFactory;

    public async ValueTask InitializeAsync() => await Cluster.DeployAsync();

    public async ValueTask DisposeAsync() => await Cluster.StopAllSilosAsync();

    private sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder silo) =>
            silo.AddMemoryGrainStorageAsDefault()
                .ConfigureServices(services =>
                {
                    services.AddSingleton<TimeProvider>(Time);
                    services.AddSingleton<ISessionArchive>(RecordingArchive.Instance);
                    services.AddSingleton<ISessionNotifier>(RecordingNotifier.Instance);
                    services.Configure<LobbyOptions>(o => {
                        o.CountInterval = TimeSpan.FromSeconds(1);
                        o.AnswerGrace = TimeSpan.FromMilliseconds(250);
                        o.HostAbsenceLimit = TimeSpan.FromMinutes(10);
                        o.ArchiveAttempts = 3;
                        o.ArchiveRetryDelay = TimeSpan.FromSeconds(1);
                    });
                });
    }
}

[CollectionDefinition(Name)]
public sealed class ClusterCollection : ICollectionFixture<ClusterFixture>
{
    public const string Name = "cluster";
}
