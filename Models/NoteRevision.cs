using SQLite;

namespace NoteTaker.Models;

public class NoteRevision
{
  [PrimaryKey, AutoIncrement]
  public int Id { get; set; }

  [Indexed]
  public int NoteId { get; set; }

  public string Content { get; set; } = string.Empty;

  public DateTime SavedAt { get; set; }

  [Ignore]
  public string DisplayText
  {
    get
    {
      var preview = string.IsNullOrWhiteSpace(Content)
          ? "(empty)"
          : Content.Replace("\n", " ").Trim();

      if (preview.Length > 60)
        preview = preview[..60] + "...";

      return $"{SavedAt.ToLocalTime():g} — {preview}";
    }
  }
}