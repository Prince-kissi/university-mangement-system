using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ComplaintSystem.Api.Filters;

public sealed class FluentValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var failures = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var argument in context.ActionArguments.Values.Where(x => x is not null))
        {
            var validatorType = typeof(IValidator<>).MakeGenericType(argument!.GetType());
            if (services.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }
            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            foreach (var error in result.Errors)
            {
                if (!failures.TryGetValue(error.PropertyName, out var messages))
                {
                    messages = [];
                    failures[error.PropertyName] = messages;
                }
                messages.Add(error.ErrorMessage);
            }
        }

        if (failures.Count > 0)
        {
            context.Result = new BadRequestObjectResult(new ValidationProblemDetails(
                failures.ToDictionary(x => x.Key, x => x.Value.Distinct().ToArray(), StringComparer.OrdinalIgnoreCase))
            {
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest
            });
            return;
        }
        await next();
    }
}
