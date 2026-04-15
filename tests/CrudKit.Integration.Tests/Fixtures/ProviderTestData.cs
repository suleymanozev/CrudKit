using Xunit;

namespace CrudKit.Integration.Tests.Fixtures;

/// <summary>
/// Provides database fixtures for [Theory] tests.
/// SQLite always runs. PostgreSQL runs only when Docker is available.
/// </summary>
public class AllProviders : TheoryData<string>
{
    public AllProviders()
    {
        Add("sqlite");
        if (SharedContainer.IsAvailable)
            Add("postgresql");
    }
}

/// <summary>
/// Manages a single shared PostgreSQL container for all tests.
/// Started lazily on first access, stopped when AppDomain unloads.
/// </summary>
public static class SharedContainer
{
    private static readonly Lazy<(string ConnectionString, bool Available)> _instance = new(() =>
    {
        try
        {
            var container = new Testcontainers.PostgreSql.PostgreSqlBuilder("postgres:17").Build();
            container.StartAsync().GetAwaiter().GetResult();
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                container.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return (container.GetConnectionString(), true);
        }
        catch
        {
            return ("", false);
        }
    });

    public static bool IsAvailable => _instance.Value.Available;
    public static string ConnectionString => _instance.Value.ConnectionString;
}

public static class FixtureFactory
{
    public static async Task<DatabaseFixture> CreateAsync(string provider)
    {
        DatabaseFixture fixture = provider switch
        {
            "postgresql" => new PostgreSqlFixture(SharedContainer.ConnectionString),
            _ => new SqliteFixture(),
        };
        await fixture.InitializeAsync();
        return fixture;
    }
}
