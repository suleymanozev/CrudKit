using System.Linq.Expressions;

namespace CrudKit.EntityFrameworkCore.Dialect;

/// <summary>Abstracts database-provider-specific SQL behaviors.</summary>
public interface IDbDialect
{
    /// <summary>Case-insensitive LIKE search. Implementation differs per provider.</summary>
    IQueryable<T> ApplyLike<T>(
        IQueryable<T> query,
        Expression<Func<T, string>> property,
        string value) where T : class;

    /// <summary>Case-insensitive starts-with search.</summary>
    IQueryable<T> ApplyStartsWith<T>(
        IQueryable<T> query,
        Expression<Func<T, string>> property,
        string value) where T : class;

    /// <summary>Generates a concurrent-safe upsert SQL statement.</summary>
    string GetUpsertSql(string table, string[] columns, string[] keyColumns);

    /// <summary>Generates SQL to fetch the next sequence value.</summary>
    string GetSequenceNextValueSql(string sequenceName);
}
