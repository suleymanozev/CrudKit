namespace CrudKit.Core.Models;

/// <summary>
/// Filter operator and value parsed from a query string parameter.
/// Supported operators: eq, neq, gt, gte, lt, lte, like, starts, in, null, notnull
/// Query string format: ?field=gte:18  ?field=like:ali  ?field=in:a,b,c  ?field=null
/// </summary>
public class FilterOp
{
    public string Operator { get; set; } = "eq";
    public string Value { get; set; } = string.Empty;
    public List<string>? Values { get; set; }

    private static readonly HashSet<string> NullaryOperators = new(StringComparer.OrdinalIgnoreCase)
        { "null", "notnull" };

    private static readonly HashSet<string> KnownOperators = new(StringComparer.OrdinalIgnoreCase)
        { "eq", "neq", "gt", "gte", "lt", "lte", "like", "starts", "in", "null", "notnull" };

    public static FilterOp Parse(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return new FilterOp { Operator = "eq", Value = "" };

        // Limit filter value length to prevent ReDoS and abuse
        if (raw.Length > 500)
            throw new ArgumentException($"Filter value too long ({raw.Length} chars). Maximum allowed: 500.");


        if (NullaryOperators.Contains(raw))
            return new FilterOp { Operator = raw.ToLowerInvariant(), Value = "" };

        var colonIdx = raw.IndexOf(':');
        if (colonIdx > 0)
        {
            var op = raw[..colonIdx];
            if (KnownOperators.Contains(op))
            {
                var val = raw[(colonIdx + 1)..];
                var result = new FilterOp { Operator = op.ToLowerInvariant(), Value = val };

                if (string.Equals(op, "in", StringComparison.OrdinalIgnoreCase))
                    result.Values = val.Split(',').ToList();

                return result;
            }
        }

        return new FilterOp { Operator = "eq", Value = raw };
    }
}
