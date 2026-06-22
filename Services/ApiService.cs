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

    private async Task SetAuthorizationHeaderAsync()
    {
        var token = await SecureStorage.Default.GetAsync("auth_token");
        if (!string.IsNullOrEmpty(token))
        {
            _httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
        else
        {
            _httpClient.DefaultRequestHeaders.Authorization = null;
        }
    }

    public async Task<List<Transaction>> GetTransactionsAsync(int? year = null, int? month = null)
    {
        await SetAuthorizationHeaderAsync();

        var url = "api/transactions";
        if (year.HasValue && month.HasValue)
        {
            url += $"?year={year.Value}&month={month.Value}";
        }

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var transactions = await response.Content.ReadFromJsonAsync<List<Transaction>>();
        return transactions ?? new List<Transaction>();
    }

    public async Task<List<Transaction>> GetTransactionsAsync(int year, int month)
    {
        return await GetTransactionsAsync((int?)year, (int?)month);
    }

    public async Task CreateTransactionAsync(TransactionDto dto)
    {
        await SetAuthorizationHeaderAsync();

        var response = await _httpClient.PostAsJsonAsync("api/transactions", dto);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteTransactionAsync(int id)
    {
        await SetAuthorizationHeaderAsync();

        var response = await _httpClient.DeleteAsync($"api/transactions/{id}");
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<BillToPay>> GetBillsAsync(string? status = null, int? month = null)
    {
        await SetAuthorizationHeaderAsync();

        var url = "api/bills";
        var queryParams = new List<string>();
        if (!string.IsNullOrEmpty(status))
        {
            queryParams.Add($"status={status}");
        }
        if (month.HasValue)
        {
            queryParams.Add($"month={month.Value}");
        }

        if (queryParams.Count > 0)
        {
            url += "?" + string.Join("&", queryParams);
        }

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var bills = await response.Content.ReadFromJsonAsync<List<BillToPay>>();
        return bills ?? new List<BillToPay>();
    }

    public async Task CreateBillAsync(BillDto dto)
    {
        await SetAuthorizationHeaderAsync();

        var response = await _httpClient.PostAsJsonAsync("api/bills", dto);
        response.EnsureSuccessStatusCode();
    }

    public async Task MarkAsPaidAsync(int id)
    {
        await SetAuthorizationHeaderAsync();

        var response = await _httpClient.PutAsync($"api/bills/{id}/pay", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteBillAsync(int id)
    {
        await SetAuthorizationHeaderAsync();

        var response = await _httpClient.DeleteAsync($"api/bills/{id}");
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<Group>> GetGroupsAsync()
    {
        await SetAuthorizationHeaderAsync();

        var response = await _httpClient.GetAsync("api/groups");
        response.EnsureSuccessStatusCode();

        var groups = await response.Content.ReadFromJsonAsync<List<Group>>();
        return groups ?? new List<Group>();
    }

    public async Task<List<User>> GetGroupMembersAsync(int groupId)
    {
        await SetAuthorizationHeaderAsync();

        var response = await _httpClient.GetAsync($"api/groups/{groupId}/members");
        response.EnsureSuccessStatusCode();

        var members = await response.Content.ReadFromJsonAsync<List<User>>();
        return members ?? new List<User>();
    }

    public async Task CreateGroupAsync(string name)
    {
        await SetAuthorizationHeaderAsync();

        var response = await _httpClient.PostAsJsonAsync("api/groups", new { Name = name });
        response.EnsureSuccessStatusCode();
    }

    public async Task<string?> AddMemberToGroupAsync(int groupId, string username)
    {
        await SetAuthorizationHeaderAsync();

        var response = await _httpClient.PostAsJsonAsync($"api/groups/{groupId}/members", new { Username = username });
        if (!response.IsSuccessStatusCode)
        {
            try
            {
                using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
                if (doc.RootElement.TryGetProperty("message", out var msgProp))
                {
                    return msgProp.GetString();
                }
            }
            catch
            {
                // ignore
            }
            return $"Erro ao adicionar membro: {response.StatusCode}";
        }
        return null;
    }

    public async Task RemoveMemberFromGroupAsync(int groupId, int memberUserId)
    {
        await SetAuthorizationHeaderAsync();

        var response = await _httpClient.DeleteAsync($"api/groups/{groupId}/members/{memberUserId}");
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteGroupAsync(int groupId)
    {
        await SetAuthorizationHeaderAsync();

        var response = await _httpClient.DeleteAsync($"api/groups/{groupId}");
        response.EnsureSuccessStatusCode();
    }

    public async Task<LoginResponse?> UpgradeToPremiumAsync()
    {
        await SetAuthorizationHeaderAsync();

        var response = await _httpClient.PostAsync("api/auth/premium/upgrade", null);
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
            if (result != null)
            {
                // Update JWT token in SecureStorage
                await SecureStorage.Default.SetAsync("auth_token", result.Token);

                // Update session
                _sessionService.StartSession(result.User, result.Token);

                return result;
            }
        }
        else
        {
            var errorMessage = await GetFriendlyErrorMessage(response);
            throw new Exception(errorMessage);
        }
        return null;
    }
}

