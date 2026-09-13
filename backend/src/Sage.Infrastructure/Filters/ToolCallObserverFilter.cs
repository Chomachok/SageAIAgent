using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.SemanticKernel;
using Sage.Core.Events;

namespace Sage.Infrastructure.Filters;

public class ToolCallObserverFilter(ChannelWriter<AgentEvent> channel) : IFunctionInvocationFilter
{
    public async Task OnFunctionInvocationAsync(
        FunctionInvocationContext context, 
        Func<FunctionInvocationContext, Task> next)
    {
        var callId = Guid.NewGuid();
        var toolName = context.Function.Name;
        var args = JsonSerializer.Serialize(context.Arguments);

        channel.TryWrite(new ToolCallStarted(toolName, args, callId));

        var sw = Stopwatch.StartNew();
        try
        {
            await next(context);
            sw.Stop();
            channel.TryWrite(new ToolCallCompleted(
                callId, 
                context.Result?.ToString(), 
                sw.Elapsed, 
                true));
        }
        catch (Exception ex)
        {
            sw.Stop();
            channel.TryWrite(new ToolCallCompleted(
                callId, 
                ex.Message, 
                sw.Elapsed, 
                false));
            throw;
        }
    }
}