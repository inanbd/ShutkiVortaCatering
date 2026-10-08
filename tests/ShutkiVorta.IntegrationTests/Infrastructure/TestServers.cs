using System.Collections.Concurrent;

namespace ShutkiVorta.IntegrationTests.Infrastructure;

/// <summary>One running application per database provider, shared by every test in the "app" collection.</summary>
public sealed class TestServers : IAsyncLifetime
{
    private readonly ConcurrentDictionary<string, AppFactory> _factories = new();

    public static TheoryData<string> Providers
    {
        get
        {
            var data = new TheoryData<string> { "Sqlite" };
            if (!string.IsNullOrWhiteSpace(AppFactory.SqlServerBaseConnection))
            {
                data.Add("SqlServer");
            }

            return data;
        }
    }

    public AppFactory Get(string provider) => _factories.GetOrAdd(provider, p => new AppFactory(p));

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var factory in _factories.Values)
        {
            await factory.DisposeAsync();
        }
    }
}

[CollectionDefinition("app")]
public sealed class AppCollection : ICollectionFixture<TestServers>;
