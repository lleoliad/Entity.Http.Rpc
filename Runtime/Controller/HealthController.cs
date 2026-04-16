using Fantasy;
using Fantasy.Network.HTTP;
using Microsoft.AspNetCore.Mvc;

namespace Entities.Http.Rpc.Controller;

[ApiController]
[Route("[controller]")]
[ServiceFilter(typeof(SceneContextFilter))]
public class HealthController(Scene scene) : ControllerBase
{
    [HttpGet]
    public IActionResult Info()
    {
        return Ok($"[{scene.SceneConfig.SceneTypeString}]: successfully.");
    }
}