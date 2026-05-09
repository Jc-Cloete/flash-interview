using FlashInterview.Application.SensitiveWords;
using FlashInterview.Web.Clients;
using FlashInterview.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace FlashInterview.Web.Controllers;

public sealed class AdminController(SensitiveWordsApiClient apiClient) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var words = await apiClient.ListAsync(cancellationToken);
        return View(new AdminViewModel { SensitiveWords = words?.Items ?? [] });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateSensitiveWordRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View("Index", new AdminViewModel { NewSensitiveWord = request });
        }

        await apiClient.CreateAsync(request, cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await apiClient.DeleteAsync(id, cancellationToken);
        return RedirectToAction(nameof(Index));
    }
}
