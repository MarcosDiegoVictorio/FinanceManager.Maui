using SQLite;

namespace FinanceManager.Maui.Models;

[Table("Users")]
public class User
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public DateTime BirthDate { get; set; }

    [Unique, MaxLength(50), Indexed(Name = "IX_Users_Username", Unique = true)]
    public string Username { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public bool IsPremium { get; set; } = false;

    public DateTime? PremiumUntil { get; set; }
}
