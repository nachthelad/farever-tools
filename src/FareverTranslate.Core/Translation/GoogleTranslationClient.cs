using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FareverTranslate.Core.Models;

namespace FareverTranslate.Core.Translation;

public sealed class GoogleTranslationClient
{
    private const string Endpoint =
        "https://translation.googleapis.com/language/translate/v2";
    private readonly HttpClient httpClient;

    public GoogleTranslationClient(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public async Task<TranslationOutcome> TranslateAsync(
        string sourceText,
        string targetLanguage,
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return TranslationOutcome.Error(
                TranslationErrorKind.MissingCredential,
                "Falta configurar la clave de Google Cloud.");
        }
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return TranslationOutcome.Error(
                TranslationErrorKind.InvalidResponse,
                "El mensaje está vacío.");
        }

        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var request = CreateRequest(
                    sourceText,
                    targetLanguage,
                    apiKey);
                using HttpResponseMessage response =
                    await httpClient.SendAsync(request, cancellationToken)
                        .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    return await ParseSuccess(response, cancellationToken)
                        .ConfigureAwait(false);
                }

                if ((int)response.StatusCode >= 500 && attempt == 0)
                {
                    await Task.Delay(350, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                return Classify(response.StatusCode);
            }
            catch (HttpRequestException) when (attempt == 0)
            {
                await Task.Delay(350, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (
                !cancellationToken.IsCancellationRequested && attempt == 0)
            {
                await Task.Delay(350, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                return TranslationOutcome.Error(
                    TranslationErrorKind.Network,
                    "No se pudo conectar con Google.");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return TranslationOutcome.Error(
                    TranslationErrorKind.Network,
                    "La traducción superó el tiempo de espera.");
            }
        }

        return TranslationOutcome.Error(
            TranslationErrorKind.Network,
            "No se pudo conectar con Google.");
    }

    private static HttpRequestMessage CreateRequest(
        string text,
        string targetLanguage,
        string apiKey)
    {
        string uri = Endpoint + "?key=" + Uri.EscapeDataString(apiKey);
        string json = JsonSerializer.Serialize(new
        {
            q = text,
            target = targetLanguage,
            format = "text",
        });
        return new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private static async Task<TranslationOutcome> ParseSuccess(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using Stream stream =
                await response.Content.ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);
            GoogleResponse? body = await JsonSerializer.DeserializeAsync<GoogleResponse>(
                stream,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            GoogleTranslation? translation = body?.Data?.Translations?.FirstOrDefault();
            if (translation?.TranslatedText is null)
            {
                return TranslationOutcome.Error(
                    TranslationErrorKind.InvalidResponse,
                    "Google devolvió una respuesta inesperada.");
            }

            return TranslationOutcome.Ok(
                WebUtility.HtmlDecode(translation.TranslatedText),
                translation.DetectedSourceLanguage);
        }
        catch (JsonException)
        {
            return TranslationOutcome.Error(
                TranslationErrorKind.InvalidResponse,
                "Google devolvió una respuesta inesperada.");
        }
    }

    private static TranslationOutcome Classify(HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                TranslationOutcome.Error(
                    TranslationErrorKind.InvalidCredential,
                    "La clave no es válida o no tiene acceso a Translation API."),
            HttpStatusCode.TooManyRequests =>
                TranslationOutcome.Error(
                    TranslationErrorKind.QuotaExceeded,
                    "Se alcanzó la cuota de Google Cloud."),
            _ when (int)statusCode >= 500 =>
                TranslationOutcome.Error(
                    TranslationErrorKind.Service,
                    "Google Translation no está disponible temporalmente."),
            _ => TranslationOutcome.Error(
                TranslationErrorKind.Service,
                $"Google rechazó la solicitud ({(int)statusCode})."),
        };

    private sealed record GoogleResponse(
        [property: JsonPropertyName("data")] GoogleData? Data);

    private sealed record GoogleData(
        [property: JsonPropertyName("translations")]
        List<GoogleTranslation>? Translations);

    private sealed record GoogleTranslation(
        [property: JsonPropertyName("translatedText")]
        string? TranslatedText,
        [property: JsonPropertyName("detectedSourceLanguage")]
        string? DetectedSourceLanguage);
}
