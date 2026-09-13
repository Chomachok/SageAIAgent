using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Sage.Core.Abstractions;
using Sage.Core.Configuration;
using Sage.Core.Events;
using Sage.Infrastructure.Agents;
using Sage.Infrastructure.Filters;
using Sage.Infrastructure.Repositories;

namespace Sage.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static void AddInfrastructure(
        this IServiceCollection services,
        LlmConfig llmConfig)
    {
        services.AddSingleton(llmConfig);
        
        services.AddSingleton<Channel<AgentEvent>>(_ => 
            Channel.CreateUnbounded<AgentEvent>());

        services.AddSingleton<ToolCallObserverFilter>(sp => 
            new ToolCallObserverFilter(
                sp.GetRequiredService<Channel<AgentEvent>>().Writer));
        
        services.AddScoped<ISessionRepository, InMemorySessionRepository>();
        services.AddScoped<ICodingAgent, SemanticKernelAgent>();
    }
}