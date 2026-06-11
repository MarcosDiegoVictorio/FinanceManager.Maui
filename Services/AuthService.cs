using FinanceManager.Maui.Models;

namespace FinanceManager.Maui.Services;

public class AuthService
{
    private readonly FinanceService _financeService;
    private readonly SessionService _sessionService;

    public AuthService(FinanceService financeService, SessionService sessionService)
    {
        _financeService = financeService;
        _sessionService = sessionService;
    }

    public async Task<RegisterResult> RegisterUserAsync(User user, string plainPassword)
    {
        // 1. Validate age (BirthDate must make them >= 18 years old)
        var today = DateTime.Today;
        var age = today.Year - user.BirthDate.Year;
        if (user.BirthDate.Date > today.AddYears(-age))
        {
            age--;
        }

        if (age < 18)
        {
            return new RegisterResult { Success = false, ErrorMessage = "Você deve ter pelo menos 18 anos de idade para se cadastrar." };
        }

        // 2. Validate inputs
        if (string.IsNullOrWhiteSpace(user.Username))
        {
            return new RegisterResult { Success = false, ErrorMessage = "O nome de usuário é obrigatório." };
        }

        if (string.IsNullOrWhiteSpace(user.Name))
        {
            return new RegisterResult { Success = false, ErrorMessage = "O nome completo é obrigatório." };
        }

        if (string.IsNullOrWhiteSpace(plainPassword) || plainPassword.Length < 6)
        {
            return new RegisterResult { Success = false, ErrorMessage = "A senha deve ter no mínimo 6 caracteres." };
        }

        // 3. Check if username already exists
        var existingUser = await _financeService.GetUserByUsernameAsync(user.Username);
        if (existingUser != null)
        {
            return new RegisterResult { Success = false, ErrorMessage = "Este nome de usuário já está em uso." };
        }

        // 4. Hash the password using BCrypt
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(plainPassword);

        // 5. Insert user into DB
        await _financeService.SaveUserAsync(user);

        return new RegisterResult { Success = true };
    }

    public async Task<LoginResult> LoginAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return new LoginResult { Success = false, ErrorMessage = "Usuário e senha são obrigatórios." };
        }

        var user = await _financeService.GetUserByUsernameAsync(username);
        if (user == null)
        {
            return new LoginResult { Success = false, ErrorMessage = "Usuário ou senha incorretos." };
        }

        // Validate hash using BCrypt
        bool isValid = BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
        if (!isValid)
        {
            return new LoginResult { Success = false, ErrorMessage = "Usuário ou senha incorretos." };
        }

        // Start session
        _sessionService.StartSession(user);

        return new LoginResult { Success = true };
    }
}

public class RegisterResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

public class LoginResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
