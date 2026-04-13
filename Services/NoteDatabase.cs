using SQLite;
using NoteTaker.Models;

namespace NoteTaker.Services;

public class NoteDatabase
{
  private SQLiteAsyncConnection? _database;
  private readonly string _dbPath;

  public NoteDatabase()
  {
    _dbPath = Path.Combine(FileSystem.AppDataDirectory, "notetaker.db3");
  }

  private async Task InitAsync()
  {
    if (_database is not null)
      return;

    _database = new SQLiteAsyncConnection(
        _dbPath,
        SQLiteOpenFlags.ReadWrite |
        SQLiteOpenFlags.Create |
        SQLiteOpenFlags.SharedCache);

    await _database.CreateTableAsync<Folder>();
    await _database.CreateTableAsync<Note>();
    await _database.CreateTableAsync<NoteRevision>();

    try
    {
      await _database.ExecuteAsync(
          "ALTER TABLE Note ADD COLUMN FolderId INTEGER NOT NULL DEFAULT 0");
    }
    catch
    {
      // Column already exists
    }

    await EnsureDefaultFolderAndMigrationAsync();
  }

  private async Task EnsureDefaultFolderAndMigrationAsync()
  {
    var folderCount = await _database!.Table<Folder>().CountAsync();

    if (folderCount == 0)
    {
      await _database.InsertAsync(new Folder
      {
        Name = "General"
      });
    }

    var firstFolder = await _database.Table<Folder>()
        .OrderBy(f => f.Id)
        .FirstOrDefaultAsync();

    if (firstFolder is not null)
    {
      await _database.ExecuteAsync(
          "UPDATE Note SET FolderId = ? WHERE FolderId = 0 OR FolderId IS NULL",
          firstFolder.Id);
    }
  }

  public async Task<List<Folder>> GetFoldersAsync()
  {
    await InitAsync();

    return await _database!
        .Table<Folder>()
        .OrderBy(f => f.Name)
        .ToListAsync();
  }

  public async Task<int> SaveFolderAsync(Folder folder)
  {
    await InitAsync();

    if (folder.Id != 0)
      return await _database!.UpdateAsync(folder);

    return await _database!.InsertAsync(folder);
  }

  public async Task<Folder?> DeleteFolderAsync(Folder folder)
  {
    await InitAsync();

    var folders = await _database!
        .Table<Folder>()
        .OrderBy(f => f.Id)
        .ToListAsync();

    if (folders.Count <= 1)
      return null;

    var fallbackFolder = folders.FirstOrDefault(f => f.Id != folder.Id);

    if (fallbackFolder is null)
      return null;

    await _database.ExecuteAsync(
        "UPDATE Note SET FolderId = ? WHERE FolderId = ?",
        fallbackFolder.Id,
        folder.Id);

    await _database.DeleteAsync(folder);

    return fallbackFolder;
  }

  public async Task<List<Note>> GetNotesAsync(int folderId)
  {
    await InitAsync();

    return await _database!
        .Table<Note>()
        .Where(n => n.FolderId == folderId)
        .OrderByDescending(n => n.UpdatedAt)
        .ToListAsync();
  }

  public async Task<Note?> GetNoteByIdAsync(int noteId)
  {
    await InitAsync();

    return await _database!
        .Table<Note>()
        .Where(n => n.Id == noteId)
        .FirstOrDefaultAsync();
  }

  public async Task<int> SaveNoteAsync(Note note)
  {
    await InitAsync();

    if (note.Id != 0)
      return await _database!.UpdateAsync(note);

    return await _database!.InsertAsync(note);
  }

  public async Task DeleteNoteAsync(Note note)
  {
    await InitAsync();

    await _database!.ExecuteAsync(
        "DELETE FROM NoteRevision WHERE NoteId = ?",
        note.Id);

    await _database.DeleteAsync(note);
  }

  public async Task AddRevisionAsync(NoteRevision revision)
  {
    await InitAsync();
    await _database!.InsertAsync(revision);
  }

  public async Task<List<NoteRevision>> GetRevisionsAsync(int noteId)
  {
    await InitAsync();

    return await _database!
        .Table<NoteRevision>()
        .Where(r => r.NoteId == noteId)
        .OrderByDescending(r => r.SavedAt)
        .ToListAsync();
  }
}