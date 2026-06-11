using SQLite;

namespace FinanceManager.Maui.Models;

[Table("Groups")]
public class Group
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public int AdminUserId { get; set; }
}
