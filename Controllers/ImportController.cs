// Controllers/ImportController.cs
using Microsoft.AspNetCore.Mvc;
using KSeF.Backend.Middleware;
using KSeF.Backend.Models.Requests;
using KSeF.Backend.Services.Interfaces.External;

namespace KSeF.Backend.Controllers;

[ApiController]
[Route("api/v1/import")]
[Produces("application/json")]
public class ImportController : ControllerBase
{
    private readonly IExternalDraftService _draftService;
    private readonly ILogger<ImportController> _logger;

    public ImportController(
        IExternalDraftService draftService,
        ILogger<ImportController> logger)
    {
        _draftService = draftService;
        _logger = logger;
    }

    [HttpPost("smartquote")]
    [ApiKeyAuth]
    public IActionResult ImportFromSmartQuote([FromBody] SmartQuoteImportRequest request)
    {
        _logger.LogInformation("=== SMARTQUOTE IMPORT REQUEST ===");
        _logger.LogInformation("SmartQuoteId: {Id}, OfferNumber: {Number}", request.SmartQuoteId, request.OfferNumber);

        var validationErrors = _draftService.Validate(request);
        if (validationErrors.Count > 0)
        {
            return BadRequest(new { success = false, message = string.Join("; ", validationErrors) });
        }

        if (_draftService.ExistsBySmartQuoteId(request.SmartQuoteId))
        {
            return Conflict(new { success = false, message = "Szkic z tym smartQuoteId już istnieje" });
        }

        var draft = _draftService.Import(request);

        _logger.LogInformation("Draft created: {DraftId} for SmartQuoteId: {SmartQuoteId}", draft.Id, draft.SmartQuoteId);

        return StatusCode(201, new
        {
            success = true,
            draftId = draft.Id,
            message = "Szkic faktury przyjęty do poczekalni"
        });
    }

    [HttpGet("drafts")]
    public IActionResult GetDrafts([FromQuery] string? status = null)
    {
        var drafts = _draftService.GetAll(status);

        return Ok(new
        {
            success = true,
            data = drafts,
            total = drafts.Count
        });
    }

    [HttpGet("drafts/{id}")]
    public IActionResult GetDraft(string id)
    {
        var draft = _draftService.GetById(id);

        if (draft == null)
            return NotFound(new { success = false, message = "Szkic nie znaleziony" });

        return Ok(new { success = true, data = draft });
    }

    [HttpPost("drafts/{id}/approve")]
    public IActionResult ApproveDraft(string id)
    {
        var draft = _draftService.Approve(id, "operator");

        if (draft == null)
            return BadRequest(new { success = false, message = "Szkic nie znaleziony lub nie ma statusu PENDING" });

        return Ok(new
        {
            success = true,
            message = "Szkic zatwierdzony",
            data = draft
        });
    }

    [HttpPost("drafts/{id}/reject")]
    public IActionResult RejectDraft(string id, [FromBody] RejectDraftRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { success = false, message = "Powód odrzucenia jest wymagany" });

        var draft = _draftService.Reject(id, "operator", request.Reason);

        if (draft == null)
            return BadRequest(new { success = false, message = "Szkic nie znaleziony lub nie ma statusu PENDING" });

        return Ok(new
        {
            success = true,
            message = "Szkic odrzucony",
            data = draft
        });
    }
}