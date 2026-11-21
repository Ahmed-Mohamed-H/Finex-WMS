using Finex.Application.Interfaces;
using Finex.Application.Services;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

namespace Finex.Application
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddFinexApplication(this IServiceCollection services)
        {
            services.AddScoped<IProductionShiftService, ProductionShiftService>();
            services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
            return services;
        }
    }
}
