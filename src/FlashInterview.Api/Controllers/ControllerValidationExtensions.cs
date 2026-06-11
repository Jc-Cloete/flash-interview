using FlashInterview.Application.Auth;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.ComponentModel.DataAnnotations;

namespace FlashInterview.Api.Controllers;

internal static class ControllerValidationExtensions
{
    public static bool TryAddDataAnnotationErrors<TRequest>(
        this ModelStateDictionary modelState,
        TRequest request)
        where TRequest : notnull
    {
        var validationResults = new List<ValidationResult>();
        var context = new ValidationContext(request);
        if (Validator.TryValidateObject(request, context, validationResults, validateAllProperties: true))
        {
            return true;
        }

        foreach (var validationResult in validationResults)
        {
            var memberNames = validationResult.MemberNames.Any()
                ? validationResult.MemberNames
                : [string.Empty];

            foreach (var memberName in memberNames)
            {
                modelState.AddModelError(memberName, validationResult.ErrorMessage ?? "The request is invalid.");
            }
        }

        return false;
    }

    public static ModelStateDictionary AddWorkflowValidationErrors(
        this ModelStateDictionary modelState,
        IReadOnlyList<AuthWorkflowValidationError>? validationErrors)
    {
        foreach (var validationError in validationErrors ?? [])
        {
            modelState.AddModelError(validationError.Key, validationError.Message);
        }

        return modelState;
    }

    public static ModelStateDictionary AddWorkflowValidationErrors(
        this ModelStateDictionary modelState,
        IReadOnlyList<UserManagementWorkflowValidationError>? validationErrors)
    {
        foreach (var validationError in validationErrors ?? [])
        {
            modelState.AddModelError(validationError.Key, validationError.Message);
        }

        return modelState;
    }
}
