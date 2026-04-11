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
        if (IsDockerAvailable())
            Add("postgresql");
    }

    private static bool IsDockerAvailable()
    {
        try
        {
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch { return false; }
    }
}

public static class FixtureFactory
{
    public static async Task<DatabaseFixture> CreateAsync(string provider)
    {
        DatabaseFixture fixture = provider switch
        {
            "postgresql" => new PostgreSqlFixture(),
            _ => new SqliteFixture(),
        };
        await fixture.InitializeAsync();
        return fixture;
    }
}
