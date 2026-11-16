using Finex.API.Middlewares;
using Finex.API.Security;
using Finex.Application.Interfaces;
using Finex.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);



builder.Services
    .AddOptions<ApiSecurityOptions>()
    .Bind(builder.Configuration.GetSection(ApiSecurityOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
    
builder.Services.AddSingleton<IValidateOptions<ApiSecurityOptions>, ApiSecurityOptionsValidator>();
builder.Services.AddFusionCache();
builder.Services.AddSingleton<IReplayProtectionStore, FusionCacheReplayProtectionStore>();


// Add services to the container.
builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
var app = builder.Build();

app.UseRouting();
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();


app.UseWhen(
    context => context.GetEndpoint()?.Metadata.GetMetadata<RequireHmacSignatureAttribute>() is not null,
    branch => branch.UseMiddleware<ApiSecurityMiddleware>());
app.UseAuthorization();

app.MapControllers();

app.Run();
