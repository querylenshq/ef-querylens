using Microsoft.Extensions.DependencyInjection;
using SampleSqlServerApp.Application.Customers;
using SampleSqlServerApp.Application.Orders;
using SampleSqlServerApp.Application.Reporting;

namespace SampleSqlServerApp.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<OrderQueries>();
        services.AddScoped<CustomerReadService>();
        services.AddScoped<CustomerReportService>();
        return services;
    }
}
