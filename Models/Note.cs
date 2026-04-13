using SQLite;

namespace NoteTaker.Models;

public class Note
{
  [PrimaryKey, AutoIncrement]
  public int Id { get; set; }

  [Indexed]
  public int FolderId { get; set; }

  [MaxLength(200)]
  public string Title { get; set; } = string.Empty;

  public string Content { get; set; } = string.Empty;

  public DateTime CreatedAt { get; set; }

  public DateTime UpdatedAt { get; set; }
}