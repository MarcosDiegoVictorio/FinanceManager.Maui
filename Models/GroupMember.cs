using SQLite;

namespace FinanceManager.Maui.Models;

[Table("GroupMembers")]
public class GroupMember
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed(Name = "IX_GroupMembers_GroupId")]
    public int GroupId { get; set; }

    [Indexed(Name = "IX_GroupMembers_UserId")]
    public int UserId { get; set; }
}
