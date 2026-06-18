using System.Net.Http.Json;
using System.Text.Json;
using FinanceManager.Maui.Models;

namespace FinanceManager.Maui.Services;

public class ApiService
{
    private readonly HttpClient _httpClient;
    private readonly SessionService _sessionService;

    public ApiService(HttpClient httpClient, SessionService sessionService)
    {
        _httpClient = httpClient;
        _sessionService = sessionService;
    }

    public async Task<LoginResult> LoginAsync(string username, string password)
    {
        try
        {
            var credentials = new { Username = username, Password = password };
            var response = await _httpClient.PostAsJsonAsync("api/auth/login", credentials);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                string token = "";
                User? user = null;

                if (!string.IsNullOrWhiteSpace(content))
                {
                    if (content.Trim().StartsWith("{"))
                    {
                        using var doc = JsonDocument.Parse(content);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("token", out var tokenProp))
                        {
                            token = tokenProp.GetString() ?? "";
                        }
                        else if (root.TryGetProperty("accessToken", out var accessTokenProp))
                        {
                            token = accessTokenProp.GetString() ?? "";
                        }

                        if (root.TryGetProperty("user", out var userProp))
                        {
                            user = JsonSerializer.Deserialize<User>(userProp.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        }
                    }
                    else
                    {
                        token = content.Trim('"');
                    }
                }

                if (string.IsNullOrEmpty(token))
                {
                    return new LoginResult { Success = false, ErrorMessage = "Token não retornado pela API." };
                }

                // Save JWT token in SecureStorage
                await SecureStorage.Default.SetAsync("auth_token", token);

                // Fallback user if not returned in API response
                user ??= new User
                {
                    Username = username,
                    Name = username
                };

                // Start session
                _sessionService.StartSession(user, token);

                return new LoginResult { Success = true };
            }
            else
            {
                var errorMessage = await GetFriendlyErrorMessage(response);
                return new LoginResult { Success = false, ErrorMessage = errorMessage };
            }
        }
        catch (Exception ex)
        {
            return new LoginResult { Success = false, ErrorMessage = $"Erro de conexão: {ex.Message}" };
        }
    }

    public async Task<RegisterResult> RegisterAsync(UserRegisterDto dto)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/auth/register", dto);

            if (response.IsSuccessStatusCode)
            {
                return new RegisterResult { Success = true };
            }
            else
            {
                var errorMessage = await GetFriendlyErrorMessage(response);
                return new RegisterResult { Success = false, ErrorMessage = errorMessage };
            }
        }
        catch (Exception ex)
        {
            return new RegisterResult { Success = false, ErrorMessage = $"Erro de conexão: {ex.Message}" };
        }
    }

    private async Task<string> GetFriendlyErrorMessage(HttpResponseMessage response)
    {
        try
        {
            var content = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(content))
            {
                return $"Erro da API: {(int)response.StatusCode} {response.ReasonPhrase}";
            }

            if (content.TrimStart().StartsWith("{"))
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                
                if (root.TryGetProperty("message", out var msgProp))
                {
                    return msgProp.GetString() ?? content;
                }
                if (root.TryGetProperty("detail", out var detailProp))
                {
                    return detailProp.GetString() ?? content;
                }
                if (root.TryGetProperty("error", out var errProp))
                {
                    return errProp.GetString() ?? content;
                }
                if (root.TryGetProperty("errors", out var errorsProp))
                {
                    if (errorsProp.ValueKind == JsonValueKind.Object)
                    {
                        var firstErrorList = errorsProp.EnumerateObject().FirstOrDefault();
                        if (firstErrorList.Value.ValueKind == JsonValueKind.Array)
                        {
                            var firstError = firstErrorList.Value.EnumerateArray().FirstOrDefault();
                            return firstError.GetString() ?? content;
                        }
                        return errorsProp.ToString();
                    }
                }
            }
            
            return content.Trim('"');
        }
        catch
        {
            return $"Erro da API: {(int)response.StatusCode} {response.ReasonPhrase}";
        }
    }
}
