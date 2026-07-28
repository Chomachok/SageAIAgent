using Microsoft.AspNetCore.Mvc;
using Sage.Core.Abstractions;
using Sage.Core.DTOs;

namespace Sage.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController(ICodingAgent agent) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Post([FromBody] ChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest("Message cannot be empty.");

        try
        {
            var response = await agent.AskAsync(request);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return NotFound(ex.Message);
        }
        catch (Exception ex)
        {
            // Логирование (позже добавим)
            return StatusCode(500, "An error occurred while processing your request.");
        }
    }
}