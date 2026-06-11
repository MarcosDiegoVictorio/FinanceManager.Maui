using SQLite;

namespace FinanceManager.Maui.Models;

[Table("Categories")]
public class Category
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed(Name = "IX_Categories_UserId")]
    public int UserId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Icon { get; set; }
}
