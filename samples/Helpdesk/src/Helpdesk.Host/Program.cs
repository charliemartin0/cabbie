using Helpdesk.Application.Sagas;
using Helpdesk.Domain;
using Helpdesk.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Cabbie;
using Cabbie.Application.Bus;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHelpdesk();
builder.UseCabbie(options =>
{
    options.HandlerAssemblies.Add(typeof(PlaceOrder).Assembly);
    options.HandlerAssemblies.Add(typeof(OrderSaga).Assembly);
});

string? connectionString = Environment.GetEnvironmentVariable("CABBIE_PG_CONNECTIONSTRING");
if (!string.IsNullOrWhiteSpace(connectionString))
{
    await builder.UseHelpdeskPostgres(connectionString);
}

var host = builder.Build();
await host.StartAsync();
await host.Services.GetRequiredService<IMessageBus>().InvokeAsync(new SendOrderReceipt(new OrderId(1)));
await host.StopAsync();