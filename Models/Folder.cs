using SQLite;

namespace NoteTaker.Models;

public class Folder
{
  [PrimaryKey, AutoIncrement]
  public int Id { get; set; }

  [Unique, MaxLength(100)]
  public string Name { get; set; } = string.Empty;
}