using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Usine.Core.Etat;

namespace Usine.Api;

internal sealed class TraducteurErreurs(IProblemDetailsService problemes) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext contexte, Exception exception, CancellationToken ct)
    {
        if (exception.TargetSite?.DeclaringType == typeof(HttpRequestJsonExtensions))
        {
            contexte.Response.StatusCode = StatusCodes.Status400BadRequest;
            return true;
        }

        if (LeveeParAspNet(exception) || Statut(exception) is not { } statut)
        {
            return false;
        }

        contexte.Response.StatusCode = statut;
        await problemes.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = contexte,
            Exception = exception,
            ProblemDetails = { Status = statut, Detail = Detail(exception) },
        });
        return true;
    }

    private static bool LeveeParAspNet(Exception exception) =>
        exception.TargetSite?.DeclaringType?.Assembly.GetName().Name?.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal) == true;

    private static string Detail(Exception exception) =>
        exception is ArgumentException { ParamName.Length: > 0 } argument && new ArgumentException("", argument.ParamName).Message is var suffixe && argument.Message.EndsWith(suffixe, StringComparison.Ordinal)
            ? argument.Message[..^suffixe.Length]
            : exception.Message;

    private static int? Statut(Exception exception) => exception switch
    {
        RefusParametresException refus => refus.Statut,
        DemandeIntrouvableException or DiscussionIntrouvableException => StatusCodes.Status404NotFound,
        ArgumentException => StatusCodes.Status400BadRequest,
        InvalidOperationException => StatusCodes.Status409Conflict,
        IOException or UnauthorizedAccessException or JsonException => StatusCodes.Status500InternalServerError,
        _ => null,
    };
}
