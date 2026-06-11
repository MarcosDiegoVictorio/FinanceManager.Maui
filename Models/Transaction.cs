using SQLite;

namespace FinanceManager.Maui.Models;

[Table("Transactions")]
public class Transaction
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed(Name = "IX_Transactions_UserId")]
    public int UserId { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal Value { get; set; }

    public string Category { get; set; } = "Geral";

    public bool IsFixed { get; set; }

    public int InstallmentNumber { get; set; } = 1;

    public int TotalInstallments { get; set; } = 1;

    public Guid? ParentId { get; set; }

    public DateTime Date { get; set; }

    [Indexed(Name = "IX_Transactions_Year_Month", Order = 1)]
    public int Year { get; set; }

    [Indexed(Name = "IX_Transactions_Year_Month", Order = 2)]
    public int Month { get; set; }

    public bool IsIncome { get; set; }

    [Indexed(Name = "IX_Transactions_GroupId")]
    public int? GroupId { get; set; }

    public bool IsPrivate { get; set; }

    [Ignore]
    public string? CreatorName { get; set; }
}
