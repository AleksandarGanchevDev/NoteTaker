using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Markdig;
using NoteTaker.Models;
using NoteTaker.Services;

namespace NoteTaker.ViewModels;

public class MainViewModel : ObservableObject
{
  private readonly NoteDatabase _database;

  private Folder? _selectedFolder;
  private Note? _selectedNote;
  private NoteRevision? _selectedRevision;

  private string _newFolderName = string.Empty;
  private string _currentTitle = string.Empty;
  private string _currentContent = string.Empty;
  private string _previewHtml = string.Empty;
  private string _statusMessage = "Ready";

  private bool _suppressTracking;
  private readonly Stack<string> _undoStack = new();
  private readonly Stack<string> _redoStack = new();

  private int _revisionLoadVersion;
  private int _notesLoadVersion;

  public ObservableCollection<Folder> Folders { get; } = new();
  public ObservableCollection<Note> Notes { get; } = new();
  public ObservableCollection<NoteRevision> Revisions { get; } = new();

  public IAsyncRelayCommand AddFolderCommand { get; }
  public IAsyncRelayCommand DeleteFolderCommand { get; }
  public IRelayCommand NewNoteCommand { get; }
  public IAsyncRelayCommand SaveNoteCommand { get; }
  public IAsyncRelayCommand DeleteNoteCommand { get; }
  public IRelayCommand UndoCommand { get; }
  public IRelayCommand RedoCommand { get; }
  public IAsyncRelayCommand RestoreRevisionCommand { get; }

  public MainViewModel(NoteDatabase database)
  {
    _database = database;

    AddFolderCommand = new AsyncRelayCommand(AddFolderAsync);
    DeleteFolderCommand = new AsyncRelayCommand(DeleteSelectedFolderAsync);
    NewNoteCommand = new RelayCommand(NewNote);
    SaveNoteCommand = new AsyncRelayCommand(SaveNoteAsync);
    DeleteNoteCommand = new AsyncRelayCommand(DeleteNoteAsync);
    UndoCommand = new RelayCommand(Undo);
    RedoCommand = new RelayCommand(Redo);
    RestoreRevisionCommand = new AsyncRelayCommand(RestoreRevisionAsync);

    UpdatePreview();
  }

  public Folder? SelectedFolder
  {
    get => _selectedFolder;
    set
    {
      if (SetProperty(ref _selectedFolder, value))
      {
        OnPropertyChanged(nameof(CanDeleteFolder));
        _ = LoadNotesAsync();
      }
    }
  }

  public Note? SelectedNote
  {
    get => _selectedNote;
    set
    {
      if (SetProperty(ref _selectedNote, value))
      {
        OnPropertyChanged(nameof(CanDelete));
        LoadSelectedNoteIntoEditor();

        if (value is null || value.Id == 0)
        {
          Revisions.Clear();
          SelectedRevision = null;
        }
        else
        {
          _ = LoadRevisionsAsync(value.Id);
        }
      }
    }
  }

  public NoteRevision? SelectedRevision
  {
    get => _selectedRevision;
    set => SetProperty(ref _selectedRevision, value);
  }

  public string NewFolderName
  {
    get => _newFolderName;
    set => SetProperty(ref _newFolderName, value);
  }

  public string CurrentTitle
  {
    get => _currentTitle;
    set => SetProperty(ref _currentTitle, value);
  }

  public string CurrentContent
  {
    get => _currentContent;
    set
    {
      var oldValue = _currentContent;

      if (SetProperty(ref _currentContent, value))
      {
        if (!_suppressTracking)
        {
          _undoStack.Push(oldValue);
          _redoStack.Clear();
          UpdateUndoRedoState();
        }

        UpdatePreview();
      }
    }
  }

  public string PreviewHtml
  {
    get => _previewHtml;
    set => SetProperty(ref _previewHtml, value);
  }

  public string StatusMessage
  {
    get => _statusMessage;
    set => SetProperty(ref _statusMessage, value);
  }

  public bool CanUndo => _undoStack.Count > 0;
  public bool CanRedo => _redoStack.Count > 0;
  public bool CanDelete => SelectedNote is not null && SelectedNote.Id > 0;
  public bool CanDeleteFolder => SelectedFolder is not null && Folders.Count > 1;

  public async Task InitializeAsync()
  {
    await LoadFoldersAsync();
  }

  public async Task LoadFoldersAsync(int? preferredFolderId = null)
  {
    var folders = await _database.GetFoldersAsync();

    Folders.Clear();
    foreach (var folder in folders)
      Folders.Add(folder);

    OnPropertyChanged(nameof(CanDeleteFolder));

    Folder? target = null;

    if (preferredFolderId.HasValue)
      target = Folders.FirstOrDefault(f => f.Id == preferredFolderId.Value);

    target ??= SelectedFolder is not null
        ? Folders.FirstOrDefault(f => f.Id == SelectedFolder.Id)
        : null;

    target ??= Folders.FirstOrDefault();

    if (target is not null)
    {
      if (SelectedFolder?.Id != target.Id)
        SelectedFolder = target;
      else
        await LoadNotesAsync();
    }
    else
    {
      SelectedFolder = null;
      Notes.Clear();
      Revisions.Clear();
      SelectedRevision = null;
      CurrentTitle = string.Empty;
      CurrentContent = string.Empty;
      UpdatePreview();
    }
  }

  public async Task LoadNotesAsync(int? preferredNoteId = null)
  {
    if (SelectedFolder is null)
    {
      Notes.Clear();
      Revisions.Clear();
      SelectedRevision = null;
      SelectedNote = null;
      NewNote();
      return;
    }

    int loadVersion = ++_notesLoadVersion;
    int folderId = SelectedFolder.Id;

    var notes = await _database.GetNotesAsync(folderId);

    if (loadVersion != _notesLoadVersion)
      return;

    if (SelectedFolder is null || SelectedFolder.Id != folderId)
      return;

    Notes.Clear();
    foreach (var note in notes)
      Notes.Add(note);

    Note? target = null;

    if (preferredNoteId.HasValue)
      target = Notes.FirstOrDefault(n => n.Id == preferredNoteId.Value);

    target ??= SelectedNote is not null
        ? Notes.FirstOrDefault(n => n.Id == SelectedNote.Id)
        : null;

    if (target is not null)
    {
      SelectedNote = target;
    }
    else if (Notes.Count > 0)
    {
      SelectedNote = Notes[0];
    }
    else
    {
      SelectedNote = null;
      Revisions.Clear();
      SelectedRevision = null;
      NewNote();
    }

    StatusMessage = $"{Notes.Count} notes in {SelectedFolder.Name}";
  }

  private void LoadSelectedNoteIntoEditor()
  {
    _suppressTracking = true;

    if (SelectedNote is null)
    {
      CurrentTitle = "Untitled";
      CurrentContent = string.Empty;
    }
    else
    {
      CurrentTitle = SelectedNote.Title;
      CurrentContent = SelectedNote.Content;
    }

    _suppressTracking = false;

    _undoStack.Clear();
    _redoStack.Clear();
    UpdateUndoRedoState();

    SelectedRevision = null;
    UpdatePreview();
  }

  private void UpdateUndoRedoState()
  {
    OnPropertyChanged(nameof(CanUndo));
    OnPropertyChanged(nameof(CanRedo));
  }

  private void UpdatePreview()
  {
    var markdown = string.IsNullOrWhiteSpace(CurrentContent)
        ? "*Nothing to preview yet.*"
        : CurrentContent;

    var htmlBody = Markdown.ToHtml(markdown);

    PreviewHtml = $$"""
        <html>
        <head>
            <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            <style>
                body {
                    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
                    padding: 16px;
                    color: #eaeaea;
                    background: #1e1e1e;
                    line-height: 1.6;
                    font-size: 16px;
                }

                h1, h2, h3, h4, h5, h6 {
                    margin-top: 1.2em;
                    margin-bottom: 0.5em;
                }

                code, pre {
                    background: #2a2a2a;
                    border-radius: 8px;
                }

                code {
                    padding: 2px 6px;
                }

                pre {
                    padding: 12px;
                    overflow-x: auto;
                }

                blockquote {
                    border-left: 4px solid #8b7cf6;
                    padding-left: 12px;
                    color: #cfcfcf;
                    margin-left: 0;
                }

                a {
                    color: #9ecbff;
                }

                hr {
                    border: none;
                    border-top: 1px solid #555;
                }
            </style>
        </head>
        <body>
            {{htmlBody}}
        </body>
        </html>
        """;
  }

  private async Task AddFolderAsync()
  {
    var folderName = NewFolderName?.Trim();

    if (string.IsNullOrWhiteSpace(folderName))
    {
      StatusMessage = "Folder name cannot be empty";
      return;
    }

    if (Folders.Any(f => string.Equals(f.Name, folderName, StringComparison.OrdinalIgnoreCase)))
    {
      StatusMessage = "Folder already exists";
      return;
    }

    var folder = new Folder
    {
      Name = folderName
    };

    await _database.SaveFolderAsync(folder);

    NewFolderName = string.Empty;
    await LoadFoldersAsync(folder.Id);

    StatusMessage = $"Folder '{folder.Name}' added";
  }

  private async Task DeleteSelectedFolderAsync()
  {
    if (SelectedFolder is null)
      return;

    var folderToDelete = SelectedFolder;

    var fallbackFolder = await _database.DeleteFolderAsync(folderToDelete);

    if (fallbackFolder is null)
    {
      StatusMessage = "You must keep at least one folder";
      return;
    }

    await LoadFoldersAsync(fallbackFolder.Id);

    StatusMessage =
        $"Folder '{folderToDelete.Name}' deleted. Notes moved to '{fallbackFolder.Name}'.";
  }

  private void NewNote()
  {
    _suppressTracking = true;

    SelectedNote = null;
    CurrentTitle = "Untitled";
    CurrentContent = string.Empty;

    _suppressTracking = false;

    _undoStack.Clear();
    _redoStack.Clear();
    UpdateUndoRedoState();

    Revisions.Clear();
    SelectedRevision = null;
    UpdatePreview();

    StatusMessage = "New note";
  }

  private async Task SaveNoteAsync()
  {
    if (SelectedFolder is null)
    {
      StatusMessage = "Create or select a folder first";
      return;
    }

    var title = string.IsNullOrWhiteSpace(CurrentTitle)
        ? "Untitled"
        : CurrentTitle.Trim();

    var content = CurrentContent ?? string.Empty;

    if (SelectedNote is not null && SelectedNote.Id > 0)
    {
      if (SelectedNote.Content != content)
      {
        await _database.AddRevisionAsync(new NoteRevision
        {
          NoteId = SelectedNote.Id,
          Content = SelectedNote.Content,
          SavedAt = DateTime.UtcNow
        });
      }

      SelectedNote.Title = title;
      SelectedNote.Content = content;
      SelectedNote.UpdatedAt = DateTime.UtcNow;
      SelectedNote.FolderId = SelectedFolder.Id;

      await _database.SaveNoteAsync(SelectedNote);
      await LoadNotesAsync(SelectedNote.Id);
    }
    else
    {
      var newNote = new Note
      {
        FolderId = SelectedFolder.Id,
        Title = title,
        Content = content,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
      };

      await _database.SaveNoteAsync(newNote);
      await LoadNotesAsync(newNote.Id);
    }

    if (SelectedNote is not null && SelectedNote.Id > 0)
      await LoadRevisionsAsync(SelectedNote.Id);

    StatusMessage = $"Saved at {DateTime.Now:t}";
  }

  private async Task DeleteNoteAsync()
  {
    if (SelectedNote is null || SelectedNote.Id == 0)
      return;

    await _database.DeleteNoteAsync(SelectedNote);
    await LoadNotesAsync();

    StatusMessage = "Note deleted";
  }

  public async Task MoveNoteToFolderAsync(int noteId, int targetFolderId)
  {
    var note = await _database.GetNoteByIdAsync(noteId);

    if (note is null)
      return;

    if (note.FolderId == targetFolderId)
    {
      StatusMessage = "Note is already in that folder";
      return;
    }

    note.FolderId = targetFolderId;
    note.UpdatedAt = DateTime.UtcNow;

    await _database.SaveNoteAsync(note);

    await LoadNotesAsync();

    var targetFolder = Folders.FirstOrDefault(f => f.Id == targetFolderId);
    StatusMessage = $"Moved to {targetFolder?.Name ?? "folder"}";
  }

  private void Undo()
  {
    if (_undoStack.Count == 0)
      return;

    _suppressTracking = true;

    _redoStack.Push(CurrentContent);
    CurrentContent = _undoStack.Pop();

    _suppressTracking = false;
    UpdateUndoRedoState();
  }

  private void Redo()
  {
    if (_redoStack.Count == 0)
      return;

    _suppressTracking = true;

    _undoStack.Push(CurrentContent);
    CurrentContent = _redoStack.Pop();

    _suppressTracking = false;
    UpdateUndoRedoState();
  }

  private async Task LoadRevisionsAsync(int noteId)
  {
    int loadVersion = ++_revisionLoadVersion;

    var revisions = await _database.GetRevisionsAsync(noteId);

    if (loadVersion != _revisionLoadVersion)
      return;

    if (SelectedNote is null || SelectedNote.Id != noteId)
      return;

    Revisions.Clear();
    SelectedRevision = null;

    foreach (var revision in revisions)
      Revisions.Add(revision);
  }

  private Task RestoreRevisionAsync()
  {
    if (SelectedRevision is null)
      return Task.CompletedTask;

    CurrentContent = SelectedRevision.Content;
    StatusMessage = "Revision loaded into editor. Press Save to keep it.";

    return Task.CompletedTask;
  }
}