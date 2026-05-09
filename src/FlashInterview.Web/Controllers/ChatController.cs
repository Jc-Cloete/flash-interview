using FlashInterview.Application.SensitiveWords;
using FlashInterview.Web.Clients;
using FlashInterview.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace FlashInterview.Web.Controllers;

public sealed class ChatController(SensitiveWordsApiClient apiClient) : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        return View(new ChatViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(string message, CancellationToken cancellationToken)
    {
        var request = new MaskMessageRequest(message);
        if (!TryValidateModel(request))
        {
            return View(new ChatViewModel { Message = message });
        }

        var result = await apiClient.MaskAsync(request, cancellationToken);
        return View(new ChatViewModel { Message = message, Result = result });
    }
}
