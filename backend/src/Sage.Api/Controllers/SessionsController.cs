using Microsoft.AspNetCore.Mvc;
using Sage.Core.Abstractions;
using Sage.Core.Entities;

namespace Sage.Api.Controllers;

[ApiController]
[Route(""api/[controller]"")]
public class SessionsController : ControllerBase
{
    private readonly ISessionRepository _repository;

    public SessionsController(ISessionRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<IActionResult> GetSessions()
    {
        var sessions = await _repository.ListAsync();
        return Ok(sessions);
    }

    [HttpPost]
    public async Task<IActionResult> CreateSession()
    {
        var session = new Session { Title = ""Новый чат"" };
        await _repository.CreateAsync(session);
        return Ok(session);
    }
}
