using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;

namespace Tahona.Api.Infrastructure;

/// <summary>Maps exceptions to RFC 7807 problem responses. Unexpected errors never leak internals.</summary>
public sealed class AppExceptionHandler(IProblemDetailsService problems, ILogger<AppExceptionHandler> logger, IHostEnvironment env)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        ProblemDetails problem;
        switch (exception)
        {
            case RequestValidationException v:
                problem = new ValidationProblemDetails(v.Errors) { Status = 400, Title = v.Message };
                break;
            case AppException app:
                problem = new ProblemDetails { Status = app.StatusCode, Title = app.Message };
                break;
            case DbUpdateConcurrencyException:
                problem = new ProblemDetails { Status = 409, Title = "This record was changed by someone else. Reload and try again." };
                break;
            case DbUpdateException db when db.InnerException?.Message.Contains("23505") == true || db.InnerException?.Message.Contains("duplicate key") == true:
                problem = new ProblemDetails { Status = 409, Title = "A record with the same unique value already exists." };
                break;
            case BadHttpRequestException bad:
                problem = new ProblemDetails { Status = bad.StatusCode, Title = "The request was invalid." };
                break;
            case OperationCanceledException when http.RequestAborted.IsCancellationRequested:
                return true;
            default:
                logger.LogError(exception, "Unhandled exception for {Method} {Path}", http.Request.Method, http.Request.Path);
                problem = new ProblemDetails
                {
                    Status = 500,
                    Title = "Something went wrong on our side. The error has been logged.",
                    Detail = env.IsDevelopment() ? exception.Message : null
                };
                break;
        }

        problem.Extensions["traceId"] = http.TraceIdentifier;
        http.Response.StatusCode = problem.Status ?? 500;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = problem, Exception = exception });
    }
}

/// <summary>Runs the FluentValidation validator for every action argument that has one, before the action executes.</summary>
public sealed class ValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var errors = new Dictionary<string, string[]>();
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null) continue;
            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (services.GetService(validatorType) is not IValidator validator) continue;

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            foreach (var group in result.Errors.GroupBy(e => ToCamel(e.PropertyName)))
                errors[group.Key] = group.Select(e => e.ErrorMessage).Distinct().ToArray();
        }

        if (errors.Count > 0) throw new RequestValidationException(errors);
        await next();
    }

    private static string ToCamel(string name) =>
        string.Join('.', name.Split('.').Select(part => part.Length == 0 ? part : char.ToLowerInvariant(part[0]) + part[1..]));
}
