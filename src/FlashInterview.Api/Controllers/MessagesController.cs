using FlashInterview.Api.SensitiveWords;
using FlashInterview.Api.Telemetry;
using FlashInterview.Application.SensitiveWords;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Swashbuckle.AspNetCore.Annotations;

namespace FlashInterview.Api.Controllers;

[ApiController]
[Route("api/messages")]
public sealed class MessagesController(
    ISensitiveWordMatcherCache matcherCache,
    MaskingMetrics maskingMetrics) : ControllerBase
{
    [HttpPost("mask")]
    [EnableRateLimiting("MaskMessage")]
    [RequestSizeLimit(16_384)]
    [SwaggerOperation(Summary = "Mask a chat message", Description = "Masks active sensitive words in a message for the mock chat client.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The masked message.", typeof(MaskMessageResponse))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "The request body failed validation.")]
    [SwaggerResponse(StatusCodes.Status413PayloadTooLarge, "The request payload is too large.")]
    [SwaggerResponse(StatusCodes.Status429TooManyRequests, "The rate limit was exceeded.")]
    public async Task<ActionResult<MaskMessageResponse>> Mask(
        [FromBody] MaskMessageRequest request,
        CancellationToken cancellationToken)
    {
        var matcher = await matcherCache.GetAsync(cancellationToken);
        var startedAt = TimeProvider.System.GetTimestamp();
        var result = matcher.Mask(request.Message);
        var elapsed = TimeProvider.System.GetElapsedTime(startedAt);
        maskingMetrics.Record(result, elapsed);

        return Ok(new MaskMessageResponse(result.OriginalMessage, result.MaskedMessage, result.Matches));
    }
}
