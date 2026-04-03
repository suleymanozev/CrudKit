using Microsoft.AspNetCore.Http;

namespace CrudKit.Core.Models;

/// <summary>
/// HTTP query string parametrelerini yapılandırılmış forma dönüştürür.
/// Page, PerPage, Sort ve Filters parametrelerini ayırır.
/// Kullanım: ?page=2&amp;per_page=25&amp;sort=-created_at&amp;name=like:ali
/// </summary>
public class ListParams
{
    private static readonly HashSet<string> ReservedKeys =
        new(StringComparer.OrdinalIgnoreCase) { "page", "per_page", "sort" };

    public int Page { get; set; } = 1;
    public int PerPage { get; set; } = 20;
    public string? Sort { get; set; }
    public Dictionary<string, FilterOp> Filters { get; set; } = new();

    public static ListParams FromQuery(IQueryCollection query)
    {
        var result = new ListParams();

        if (query.TryGetValue("page", out var pageVal) && int.TryParse(pageVal, out var page) && page > 0)
            result.Page = page;

        if (query.TryGetValue("per_page", out var ppVal) && int.TryParse(ppVal, out var pp) && pp > 0)
            result.PerPage = Math.Min(pp, 100);

        if (query.TryGetValue("sort", out var sortVal))
            result.Sort = sortVal.ToString();

        foreach (var key in query.Keys)
        {
            if (ReservedKeys.Contains(key)) continue;
            var raw = query[key].ToString();
            result.Filters[key] = FilterOp.Parse(raw);
        }

        return result;
    }
}
