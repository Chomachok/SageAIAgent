using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sage.Core.Abstractions;
using Sage.Core.Configuration;
using Sage.Infrastructure.Agents;
using Sage.Infrastructure.Data;
using Sage.Infrastructure.Repositories;

namespace Sage.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static void AddInfrastructure(
        this IServiceCollection services,
        LlmConfig llmConfig,
        string? connectionString = null)
    {
        services.AddSingleton(llmConfig);
        services.AddSingleton<IOptions<LlmConfig>>(sp =>
            Options.Create(sp.GetRequiredService<LlmConfig>()));

        if (!string.IsNullOrEmpty(connectionString))
        {
            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(connectionString));
        }

        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<ICodingAgent, SemanticKernelAgent>();
    }
}