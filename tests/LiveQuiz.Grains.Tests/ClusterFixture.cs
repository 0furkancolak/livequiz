using LiveQuiz.Grains.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;
using Orleans.TestingHost;

namespace LiveQuiz.Grains.Tests;

public sealed class ClusterFixture : IAsyncLifetime
{
    public TestCluster Cluster { get; } = new TestClusterBuilder(1)
        .AddSiloBuilderConfigurator<SiloConfigurator>()
        .Build();

    public IGrainFactory Grains => Cluster.GrainFactory;

    public async ValueTask InitializeAsync() => await Cluster.DeployAsync();

    public async ValueTask DisposeAsync() => await Cluster.StopAllSilosAsync();

    private sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder silo) =>
            silo.AddMemoryGrainStorageAsDefault()
                .ConfigureServices(services =>
                {
                    services.TryAddSingleton(TimeProvider.System);
                    services.AddSingleton<ISessionNotifier>(RecordingNotifier.Instance);
                    services.Configure<LobbyOptions>(o => o.CountInterval = TimeSpan.FromMilliseconds(300));
                });
    }
}

[CollectionDefinition(Name)]
public sealed class ClusterCollection : ICollectionFixture<ClusterFixture>
{
    public const string Name = "cluster";
}
