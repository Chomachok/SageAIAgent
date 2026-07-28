using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sage.Core.Abstractions;
using Sage.Core.Configuration;
using Sage.Infrastructure.Agents;
using Sage.Infrastructure.Data;
using Sage.Infrastructure.Repositories;

namespace Sage.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Конфигурация LLM
        services.Configure<LlmOptions>(configuration.GetSection("LLM"));

        // База данных
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString));

        // Репозитории
        services.AddScoped<ISessionRepository, SessionRepository>();

        // Агент
        services.AddScoped<ICodingAgent, SemanticKernelAgent>();

        return services;
    }
}