using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PersonalFinances.Api.Data;
using PersonalFinances.Api.Models;

namespace PersonalFinances.Api.Services;

public sealed class GoogleDriveService(IHttpClientFactory httpClientFactory, FinanceDbContext db)
{
    private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string UserInfoEndpoint = "https://www.googleapis.com/oauth2/v2/userinfo";
    private const string DriveFilesEndpoint = "https://www.googleapis.com/drive/v3/files";
    private const string DriveUploadEndpoint = "https://www.googleapis.com/upload/drive/v3/files";
    private const string BackupFolderName = "PersonalFinances Backups";
    private const string Scopes = "https://www.googleapis.com/auth/drive.file https://www.googleapis.com/auth/userinfo.email";

    public async Task<DriveSettings> GetOrCreateSettingsAsync()
    {
        var settings = await db.DriveSettings.FirstOrDefaultAsync(item => item.Id == 1);
        if (settings is not null)
        {
            return settings;
        }

        settings = new DriveSettings { Id = 1 };
        db.DriveSettings.Add(settings);
        await db.SaveChangesAsync();
        return settings;
    }

    public Task SaveAsync() => db.SaveChangesAsync();

    public string BuildAuthorizationUrl(string clientId, string redirectUri)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = Scopes,
            ["access_type"] = "offline",
            ["prompt"] = "consent",
        };

        var queryString = string.Join(
            "&",
            query.Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value)}"));
        return $"{AuthEndpoint}?{queryString}";
    }

    public async Task ExchangeCodeAsync(string code, string redirectUri)
    {
        var settings = await GetOrCreateSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            throw new InvalidOperationException("Falten les credencials de Google configurades.");
        }

        var http = httpClientFactory.CreateClient();
        var response = await http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = settings.ClientId,
            ["client_secret"] = settings.ClientSecret,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
        }));

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Google ha rebutjat l'intercanvi del codi: {body}");
        }

        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>();
        if (string.IsNullOrWhiteSpace(payload?.RefreshToken))
        {
            throw new InvalidOperationException(
                "Google no ha retornat un refresh token. Revoca l'accés a l'app des del teu compte de Google i torna-ho a provar.");
        }

        settings.RefreshToken = payload.RefreshToken;

        using var userInfoRequest = new HttpRequestMessage(HttpMethod.Get, UserInfoEndpoint);
        userInfoRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", payload.AccessToken);
        var userInfoResponse = await http.SendAsync(userInfoRequest);
        if (userInfoResponse.IsSuccessStatusCode)
        {
            var userInfo = await userInfoResponse.Content.ReadFromJsonAsync<UserInfoResponse>();
            settings.ConnectedAccountEmail = userInfo?.Email;
        }

        await db.SaveChangesAsync();
    }

    public async Task DisconnectAsync()
    {
        var settings = await GetOrCreateSettingsAsync();
        settings.RefreshToken = null;
        settings.ConnectedAccountEmail = null;
        settings.FolderId = null;
        await db.SaveChangesAsync();
    }

    public async Task UploadBackupAsync(string filePath, string fileName)
    {
        var settings = await GetOrCreateSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.RefreshToken)
            || string.IsNullOrWhiteSpace(settings.ClientId)
            || string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            throw new InvalidOperationException("Google Drive no està connectat.");
        }

        var http = httpClientFactory.CreateClient();
        var accessToken = await GetAccessTokenAsync(http, settings);
        var folderId = await EnsureBackupFolderAsync(http, accessToken, settings);

        var metadataJson = JsonSerializer.Serialize(new { name = fileName, parents = new[] { folderId } });

        using var content = new MultipartContent("related");
        var metadataContent = new StringContent(metadataJson, System.Text.Encoding.UTF8, "application/json");
        content.Add(metadataContent);

        await using var fileStream = File.OpenRead(filePath);
        using var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent);

        using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, $"{DriveUploadEndpoint}?uploadType=multipart")
        {
            Content = content,
        };
        uploadRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var uploadResponse = await http.SendAsync(uploadRequest);
        if (!uploadResponse.IsSuccessStatusCode)
        {
            var body = await uploadResponse.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"No s'ha pogut pujar la còpia a Drive: {body}");
        }
    }

    private static async Task<string> GetAccessTokenAsync(HttpClient http, DriveSettings settings)
    {
        var response = await http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = settings.ClientId!,
            ["client_secret"] = settings.ClientSecret!,
            ["refresh_token"] = settings.RefreshToken!,
            ["grant_type"] = "refresh_token",
        }));

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"No s'ha pogut renovar l'accés a Google Drive: {body}");
        }

        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>();
        return payload?.AccessToken
            ?? throw new InvalidOperationException("Google no ha retornat un token d'accés.");
    }

    private async Task<string> EnsureBackupFolderAsync(HttpClient http, string accessToken, DriveSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.FolderId))
        {
            return settings.FolderId;
        }

        var query = Uri.EscapeDataString(
            $"mimeType='application/vnd.google-apps.folder' and name='{BackupFolderName}' and trashed=false");
        using var searchRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"{DriveFilesEndpoint}?q={query}&spaces=drive&fields=files(id,name)");
        searchRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var searchResponse = await http.SendAsync(searchRequest);
        searchResponse.EnsureSuccessStatusCode();
        var searchResult = await searchResponse.Content.ReadFromJsonAsync<FileListResponse>();
        var existing = searchResult?.Files?.FirstOrDefault();
        if (existing is not null)
        {
            settings.FolderId = existing.Id;
            await db.SaveChangesAsync();
            return existing.Id;
        }

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, DriveFilesEndpoint)
        {
            Content = JsonContent.Create(new { name = BackupFolderName, mimeType = "application/vnd.google-apps.folder" }),
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var createResponse = await http.SendAsync(createRequest);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<DriveFile>();
        var folderId = created?.Id ?? throw new InvalidOperationException("No s'ha pogut crear la carpeta a Drive.");

        settings.FolderId = folderId;
        await db.SaveChangesAsync();
        return folderId;
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record UserInfoResponse(
        [property: JsonPropertyName("email")] string? Email);

    private sealed record DriveFile(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string? Name);

    private sealed record FileListResponse(
        [property: JsonPropertyName("files")] IReadOnlyList<DriveFile>? Files);
}
