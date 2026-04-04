using System.Reflection;

namespace CrudKit.Api.Configuration;

public class CrudKitApiOptions
{
    public int DefaultPageSize { get; set; } = 20;
    public int MaxPageSize { get; set; } = 100;
    public string ApiPrefix { get; set; } = "/api";
    public int BulkLimit { get; set; } = 10_000;
    public Assembly? ScanModulesFromAssembly { get; set; }
    public bool EnableIdempotency { get; set; }
}
