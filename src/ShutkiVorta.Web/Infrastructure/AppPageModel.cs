using System.Reflection;
using MediatR;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShutkiVorta.Application.Common.Exceptions;
using ShutkiVorta.Domain.Common;

namespace ShutkiVorta.Web.Infrastructure;

/// <summary>Base page model: sends MediatR requests and turns validation failures into ModelState errors.</summary>
public abstract class AppPageModel(ISender sender) : PageModel
{
    protected ISender Sender { get; } = sender;

    [Microsoft.AspNetCore.Mvc.TempData]
    public string? StatusMessage { get; set; }

    [Microsoft.AspNetCore.Mvc.TempData]
    public string? ErrorMessage { get; set; }

    /// <summary>Runs <paramref name="action"/>; on validation errors adds them to ModelState (mapped onto "Input.*" fields) and returns false.</summary>
    protected async Task<bool> TryExecuteAsync(Func<Task> action, Type? inputType = null, string prefix = "Input")
    {
        try
        {
            await action();
            return true;
        }
        catch (ValidationException ex)
        {
            AddErrors(ex, inputType, prefix);
            return false;
        }
        catch (DomainException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return false;
        }
    }

    protected void AddErrors(ValidationException ex, Type? inputType, string prefix = "Input")
    {
        foreach (var (key, messages) in ex.Errors)
        {
            var target = !string.IsNullOrEmpty(key) && inputType?.GetProperty(key, BindingFlags.Public | BindingFlags.Instance) is not null
                ? $"{prefix}.{key}"
                : string.Empty;

            foreach (var message in messages)
            {
                ModelState.AddModelError(target, message);
            }
        }
    }

    protected void AddErrors(IEnumerable<string> errors)
    {
        foreach (var error in errors)
        {
            ModelState.AddModelError(string.Empty, error);
        }
    }

    protected string? SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : null;
}
