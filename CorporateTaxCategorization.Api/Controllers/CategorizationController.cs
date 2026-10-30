using System.ComponentModel.DataAnnotations;
using CorporateTaxCategorization.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CorporateTaxCategorization.Api.Controllers;

public sealed class SuggestionRequest
{
    [Required, StringLength(32_000, MinimumLength = 1)]
    public string Description { get; init; } = string.Empty;
    [Range(1, 6)]
    public int Count { get; init; } = 3;
}

[ApiController]
[Route("api/categorization")]
public sealed class CategorizationController(CategorizationService service) : ControllerBase
{
    [HttpGet("examples")]
    public ActionResult<IReadOnlyList<ExampleResponse>> GetExamples() => Ok(service.GetExamples());

    /// <summary>Returns similar examples; similarity is not a calibrated probability.</summary>
    [HttpPost("suggestions")]
    [ProducesResponseType<SuggestionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public ActionResult<SuggestionResponse> GetSuggestions(SuggestionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Description))
        {
            ModelState.AddModelError(nameof(request.Description), "A ledger description is required.");
            return ValidationProblem(ModelState);
        }
        return Ok(service.Suggest(request.Description, request.Count));
    }
}
