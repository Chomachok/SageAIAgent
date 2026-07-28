using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sage.Core.Abstractions;
using Sage.Infrastructure.Data;
using Sage.Infrastructure.Repositories;

namespace Sage.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString = configuration.GetConnectionString(""DefaultConnection"");
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<ISessionRepository, SessionRepository>();

        // Агент будет добавлен позже
        // services.AddScoped<ICodingAgent, SemanticKernelAgent>();

        return services;
    }
}
