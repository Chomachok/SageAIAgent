using Microsoft.AspNetCore.Mvc;
using Sage.Core.Abstractions;
using Sage.Core.Entities;

namespace Sage.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SessionsController(ISessionRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetSessions()
    {
        var sessions = await repository.ListAsync();
        return Ok(sessions);
    }

    [HttpPost]
    public async Task<IActionResult> CreateSession()
    {
        var session = new Session { Title = "Новый чат" };
        await repository.CreateAsync(session);
        return Ok(session);
    }
}
