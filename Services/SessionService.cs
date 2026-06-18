using FinanceManager.Maui.Models;

namespace FinanceManager.Maui.Services;

public class SessionService
{
    public User? CurrentUser { get; private set; }
    public string? Token { get; private set; }
    public DateTime LastActivityTime { get; private set; }

    public void StartSession(User user)
    {
        StartSession(user, Guid.NewGuid().ToString());
    }

    public void StartSession(User user, string token)
    {
        CurrentUser = user;
        Token = token;
        LastActivityTime = DateTime.UtcNow;
    }

    public void ClearSession()
    {
        CurrentUser = null;
        Token = null;
        LastActivityTime = DateTime.MinValue;
    }

    public bool ValidateSession()
    {
        if (CurrentUser == null || Token == null)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        if ((now - LastActivityTime).TotalMinutes > 1)
        {
            ClearSession();
            return false;
        }

        LastActivityTime = now;
        return true;
    }

    public void RefreshActivity()
    {
        if (CurrentUser != null && Token != null)
        {
            LastActivityTime = DateTime.UtcNow;
        }
    }
}
