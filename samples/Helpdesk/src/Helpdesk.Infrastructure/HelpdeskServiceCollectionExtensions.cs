using Helpdesk.Application.Receipts;
using Microsoft.Extensions.DependencyInjection;

namespace Helpdesk.Infrastructure;

/// <summary>
/// Registers the Helpdesk services Cabbie handlers depend on.
/// </summary>
public static class HelpdeskServiceCollectionExtensions
{
    public static IServiceCollection AddHelpdesk(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IReceiptSender, LoggingReceiptSender>();
        return services;
    }
}
