using FlashInterview.Application.SensitiveWords;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace FlashInterview.Api.Controllers;

[ApiController]
[Route("api/sensitive-words")]
public sealed class SensitiveWordsController(ISensitiveWordRepository repository) : ControllerBase
{
    [HttpPost]
    [SwaggerOperation(Summary = "Create a sensitive word", Description = "Creates a new sensitive word for internal administration.")]
    [SwaggerResponse(StatusCodes.Status201Created, "The sensitive word was created.", typeof(SensitiveWordDto))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "The request body failed validation.")]
    public async Task<ActionResult<SensitiveWordDto>> Create(
        [FromBody] CreateSensitiveWordRequest request,
        CancellationToken cancellationToken)
    {
        var created = await repository.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpGet]
    [SwaggerOperation(Summary = "List sensitive words", Description = "Lists sensitive words with optional filtering and pagination.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The paged sensitive-word list.", typeof(PagedResponse<SensitiveWordDto>))]
    public async Task<ActionResult<PagedResponse<SensitiveWordDto>>> List(
        [FromQuery] string? q,
        [FromQuery] string? category,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await repository.ListAsync(new SensitiveWordQuery(q, category, isActive, page, pageSize), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [SwaggerOperation(Summary = "Get a sensitive word", Description = "Gets one sensitive word by identifier.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The sensitive word was found.", typeof(SensitiveWordDto))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No sensitive word exists for the supplied identifier.")]
    public async Task<ActionResult<SensitiveWordDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await repository.GetAsync(id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPut("{id:guid}")]
    [SwaggerOperation(Summary = "Update a sensitive word", Description = "Updates the value, category, and active status for a sensitive word.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The sensitive word was updated.", typeof(SensitiveWordDto))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "The request body failed validation.")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No sensitive word exists for the supplied identifier.")]
    public async Task<ActionResult<SensitiveWordDto>> Update(
        Guid id,
        [FromBody] UpdateSensitiveWordRequest request,
        CancellationToken cancellationToken)
    {
        var result = await repository.UpdateAsync(id, request, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [SwaggerOperation(Summary = "Delete a sensitive word", Description = "Deletes a sensitive word from the internal administration list.")]
    [SwaggerResponse(StatusCodes.Status204NoContent, "The sensitive word was deleted.")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No sensitive word exists for the supplied identifier.")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await repository.DeleteAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}
