using Microsoft.AspNetCore.Mvc;
using rag_example.Models;
using rag_example.Service;

namespace rag_example.Controllers;

[ApiController]
[Route("[controller]")]
public class PolicyController(PolicyService policyService) : ControllerBase
{
    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] QuestionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest("Question cannot be empty.");

        var answer = await policyService.GetAnswerAsync(request.Question);
        return Ok(new { answer });
    }
}
