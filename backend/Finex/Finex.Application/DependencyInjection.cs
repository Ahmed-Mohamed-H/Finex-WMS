using Finex.Application.Interfaces.Catalog;
using Finex.Application.Interfaces.Production;
using Finex.Application.Services.Catalog;
using Finex.Application.Services.Production;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Finex.Application
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddFinexApplication(this IServiceCollection services)
        {
            services.AddScoped<IProductionShiftService, ProductionShiftService>();
            services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
            services.AddScoped<ICatalogService, CatalogService>();
            return services;
        }
    }
}