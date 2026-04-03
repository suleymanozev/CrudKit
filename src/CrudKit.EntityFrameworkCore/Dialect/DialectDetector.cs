using Microsoft.EntityFrameworkCore;

namespace CrudKit.EntityFrameworkCore.Dialect;

/// <summary>
/// Auto-detects the database dialect from the EF Core provider name.
/// Called by ServiceCollectionExtensions — users never need to call this directly.
/// </summary>
public static class DialectDetector
{
    public static IDbDialect Detect(DbContext db)
    {
        var provider = db.Database.ProviderName ?? string.Empty;
        return provider switch
        {
            _ when provider.Contains("Npgsql")    => new PostgresDialect(),
            _ when provider.Contains("SqlServer") => new SqlServerDialect(),
            _ when provider.Contains("Sqlite")    => new SqliteDialect(),
            _                                      => new GenericDialect(),
        };
    }
}
