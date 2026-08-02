using PersonalFinances.Api.Contracts;
using PersonalFinances.Api.Services;

namespace PersonalFinances.Api.Endpoints;

public static class DriveEndpoints
{
    public static IEndpointRouteBuilder MapDriveEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/drive");

        group.MapGet("/status", async (GoogleDriveService drive) =>
        {
            var settings = await drive.GetOrCreateSettingsAsync();
            return Results.Ok(new DriveStatusDto(
                HasCredentials: !string.IsNullOrWhiteSpace(settings.ClientId) && !string.IsNullOrWhiteSpace(settings.ClientSecret),
                ClientId: settings.ClientId,
                Connected: !string.IsNullOrWhiteSpace(settings.RefreshToken),
                ConnectedAccountEmail: settings.ConnectedAccountEmail,
                AutoUpload: settings.AutoUpload));
        });

        group.MapPost("/settings", async (SaveDriveSettingsRequest request, GoogleDriveService drive) =>
        {
            if (string.IsNullOrWhiteSpace(request.ClientId))
            {
                return Results.BadRequest("El Client ID és obligatori.");
            }

            var settings = await drive.GetOrCreateSettingsAsync();
            settings.ClientId = request.ClientId.Trim();
            if (!string.IsNullOrWhiteSpace(request.ClientSecret))
            {
                settings.ClientSecret = request.ClientSecret.Trim();
            }
            settings.AutoUpload = request.AutoUpload;
            await drive.SaveAsync();

            return Results.NoContent();
        });

        group.MapGet("/connect", async (HttpRequest request, GoogleDriveService drive) =>
        {
            var settings = await drive.GetOrCreateSettingsAsync();
            if (string.IsNullOrWhiteSpace(settings.ClientId))
            {
                return Results.BadRequest("Configura primer el Client ID i el Client Secret.");
            }

            var redirectUri = BuildRedirectUri(request);
            var url = drive.BuildAuthorizationUrl(settings.ClientId, redirectUri);
            return Results.Redirect(url);
        });

        group.MapGet("/oauth-callback", async (HttpRequest request, GoogleDriveService drive) =>
        {
            var code = request.Query["code"].ToString();
            var error = request.Query["error"].ToString();

            if (!string.IsNullOrWhiteSpace(error))
            {
                return Results.Content(BuildCallbackPage(false, $"Google ha retornat un error: {error}"), "text/html");
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                return Results.Content(BuildCallbackPage(false, "No s'ha rebut cap codi d'autorització."), "text/html");
            }

            try
            {
                var redirectUri = BuildRedirectUri(request);
                await drive.ExchangeCodeAsync(code, redirectUri);
                return Results.Content(BuildCallbackPage(true, "Connexió amb Google Drive completada correctament."), "text/html");
            }
            catch (Exception ex)
            {
                return Results.Content(BuildCallbackPage(false, ex.Message), "text/html");
            }
        });

        group.MapPost("/disconnect", async (GoogleDriveService drive) =>
        {
            await drive.DisconnectAsync();
            return Results.NoContent();
        });

        return app;
    }

    private static string BuildRedirectUri(HttpRequest request) =>
        $"{request.Scheme}://{request.Host}/api/drive/oauth-callback";

    private static string BuildCallbackPage(bool success, string message) => $$"""
        <!doctype html>
        <html lang="ca">
        <head>
        <meta charset="utf-8" />
        <title>Google Drive</title>
        <style>
            body { font-family: system-ui, sans-serif; display: grid; place-items: center; height: 100vh; margin: 0; background: #f8fafc; }
            .card { max-width: 420px; padding: 32px; border-radius: 16px; background: #fff; box-shadow: 0 12px 35px rgb(30 41 59 / 12%); text-align: center; }
            h1 { font-size: 1.2rem; color: {{(success ? "#047857" : "#b91c1c")}}; }
            p { color: #475569; }
        </style>
        </head>
        <body>
            <div class="card">
                <h1>{{(success ? "Connectat!" : "Hi ha hagut un problema")}}</h1>
                <p>{{message}}</p>
                <p>Ja pots tancar aquesta finestra.</p>
            </div>
        </body>
        </html>
        """;
}
