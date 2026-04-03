using CrudKit.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.EntityFrameworkCore.Query;

/// <summary>
/// Orchestrates filtering, counting, sorting, and pagination into a Paginated&lt;T&gt; result.
/// </summary>
public class QueryBuilder<T> where T : class
{
    private readonly FilterApplier _filterApplier;

    public QueryBuilder(FilterApplier filterApplier)
        => _filterApplier = filterApplier;

    public async Task<Paginated<T>> Apply(
        IQueryable<T> query,
        ListParams listParams,
        CancellationToken ct = default)
    {
        // 1. Apply [DefaultInclude] navigation properties
        query = IncludeApplier.Apply(query);

        // 2. Apply filters
        foreach (var (field, op) in listParams.Filters)
            query = _filterApplier.Apply(query, field, op);

        // 3. Count after filtering (before pagination)
        var total = await query.CountAsync(ct);

        // 4. Apply sort
        query = SortApplier.Apply(query, listParams.Sort);

        // 5. Paginate
        var data = await query
            .Skip((listParams.Page - 1) * listParams.PerPage)
            .Take(listParams.PerPage)
            .ToListAsync(ct);

        return new Paginated<T>
        {
            Data = data,
            Total = total,
            Page = listParams.Page,
            PerPage = listParams.PerPage,
            TotalPages = (int)Math.Ceiling((double)total / listParams.PerPage),
        };
    }
}
