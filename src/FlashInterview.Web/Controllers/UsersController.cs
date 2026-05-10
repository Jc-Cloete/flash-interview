using FlashInterview.Application.Auth;
using FlashInterview.Web.Auth;
using FlashInterview.Web.Clients;
using FlashInterview.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlashInterview.Web.Controllers;

[Authorize(Policy = AdminAuthorizationPolicies.SuperAdmin)]
public sealed class UsersController(
    AuthApiClient authApiClient,
    ILogger<UsersController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var users = await GetUserViewModelsAsync(cancellationToken);
        return View(new UserManagementIndexViewModel { Users = users });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id, CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        return View(UserManagementEditViewModel.FromUser(user));
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new UserManagementCreateViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        UserManagementCreateViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await authApiClient.CreateUserAsync(
            new CreateUserRequest(model.Email, model.DisplayName, model.Password, model.IsAdmin),
            cancellationToken);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "The user could not be created. Check that the email is unique and the password meets policy.");
            return View(model);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateAdminRole(
        UserManagementRoleUpdateViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            logger.LogWarning(
                "Rejected admin-role update because form binding failed. ModelStateErrors={ModelStateErrors}",
                CreateModelStateErrorSummary());
            return BadRequest(new { message = "Invalid admin-role update form.", errors = CreateModelStateErrorSummary() });
        }

        var user = await authApiClient.UpdateAdminRoleAsync(
            model.Id,
            new UserRoleUpdateRequest(model.IsAdmin),
            cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task<IReadOnlyList<UserManagementUserViewModel>> GetUserViewModelsAsync(CancellationToken cancellationToken)
    {
        var users = await authApiClient.ListUsersAsync(cancellationToken);
        return users.Select(UserManagementUserViewModel.FromDto).ToArray();
    }

    private async Task<UserManagementUserViewModel?> FindUserAsync(string id, CancellationToken cancellationToken)
    {
        var users = await GetUserViewModelsAsync(cancellationToken);
        return users.FirstOrDefault(user => string.Equals(user.Id, id, StringComparison.Ordinal));
    }

    private IReadOnlyList<object> CreateModelStateErrorSummary()
    {
        return ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .Select(entry => new
            {
                Field = entry.Key,
                AttemptedValue = entry.Value?.AttemptedValue,
                Errors = entry.Value?.Errors.Select(error => error.ErrorMessage).ToArray() ?? []
            })
            .ToArray();
    }
}
