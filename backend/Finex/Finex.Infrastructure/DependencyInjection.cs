using Finex.Application.Interfaces;
using Finex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Finex.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddFinexInfrastructure(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString, nameof(connectionString));

            services.AddDbContext<FinexDbContext>(options =>
            {
                options.UseSqlServer(connectionString, sqlOptions =>
                {
                    sqlOptions.MigrationsAssembly(typeof(FinexDbContext).Assembly.FullName);
                    sqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(5),
                        errorNumbersToAdd: null);
                });
            });

            services.AddScoped<IFinexDbContext>(sp => sp.GetRequiredService<FinexDbContext>());

            return services;
        }
    }
}