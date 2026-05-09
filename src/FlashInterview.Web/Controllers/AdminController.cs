using FlashInterview.Application.SensitiveWords;
using FlashInterview.Web.Clients;
using FlashInterview.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace FlashInterview.Web.Controllers;

public sealed class AdminController(SensitiveWordsApiClient apiClient) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        string? q,
        string? category,
        bool? isActive,
        CancellationToken cancellationToken)
    {
        return View(await BuildViewModelAsync(q, category, isActive, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        CreateSensitiveWordRequest request,
        string? q,
        string? category,
        bool? isActive,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View("Index", await BuildViewModelAsync(q, category, isActive, cancellationToken, request));
        }

        try
        {
            await apiClient.CreateAsync(request, cancellationToken);
        }
        catch (ApiValidationException exception)
        {
            AddValidationErrors(exception);
            return View("Index", await BuildViewModelAsync(q, category, isActive, cancellationToken, request));
        }

        return RedirectToAction(nameof(Index), new { q, category, isActive });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateSensitiveWordRequest request,
        string? q,
        string? category,
        bool? isActive,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View("Index", await BuildViewModelAsync(q, category, isActive, cancellationToken));
        }

        try
        {
            await apiClient.UpdateAsync(id, request, cancellationToken);
        }
        catch (ApiValidationException exception)
        {
            AddValidationErrors(exception);
            return View("Index", await BuildViewModelAsync(q, category, isActive, cancellationToken));
        }

        return RedirectToAction(nameof(Index), new { q, category, isActive });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(
        Guid id,
        string value,
        string? wordCategory,
        string? q,
        string? category,
        bool? isActive,
        CancellationToken cancellationToken)
    {
        try
        {
            await apiClient.UpdateAsync(id, new UpdateSensitiveWordRequest(value, wordCategory, false), cancellationToken);
        }
        catch (ApiValidationException exception)
        {
            AddValidationErrors(exception);
            return View("Index", await BuildViewModelAsync(q, category, isActive, cancellationToken));
        }

        return RedirectToAction(nameof(Index), new { q, category, isActive });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(
        Guid id,
        string? q,
        string? category,
        bool? isActive,
        CancellationToken cancellationToken)
    {
        await apiClient.DeleteAsync(id, cancellationToken);
        return RedirectToAction(nameof(Index), new { q, category, isActive });
    }

    private async Task<AdminViewModel> BuildViewModelAsync(
        string? q,
        string? category,
        bool? isActive,
        CancellationToken cancellationToken,
        CreateSensitiveWordRequest? newSensitiveWord = null)
    {
        var filter = new AdminFilterViewModel
        {
            Q = q,
            Category = category,
            IsActive = isActive
        };
        var words = await apiClient.ListAsync(
            new SensitiveWordQuery(q, category, isActive),
            cancellationToken);

        return new AdminViewModel
        {
            SensitiveWords = words?.Items ?? [],
            Filter = filter,
            NewSensitiveWord = newSensitiveWord ?? new CreateSensitiveWordRequest("", "sql", true)
        };
    }

    private void AddValidationErrors(ApiValidationException exception)
    {
        foreach (var (field, messages) in exception.Errors)
        {
            foreach (var message in messages)
            {
                ModelState.AddModelError(field, message);
            }
        }
    }
}
