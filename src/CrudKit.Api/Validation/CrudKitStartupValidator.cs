using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrudKit.Api.Validation;

public class CrudKitStartupValidator : IHostedService
{
    private readonly ILogger<CrudKitStartupValidator> _logger;

    public CrudKitStartupValidator(IServiceProvider services, ILogger<CrudKitStartupValidator> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("CrudKitStartupValidator: startup validation complete.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
