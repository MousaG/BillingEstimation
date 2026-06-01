using ElectricityConsumptionForecasting.Application.Interfaces;
using ElectricityConsumptionForecasting.Infrastructure.Persistence;
using ElectricityConsumptionForecasting.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ElectricityConsumptionForecasting.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("ForecastingDb")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=ElectricityConsumptionForecasting;Trusted_Connection=True;TrustServerCertificate=True";

        services.AddDbContext<ForecastingDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IForecastDataStore, EfForecastDataStore>();
        return services;
    }
}
