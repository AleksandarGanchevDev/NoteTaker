using Microsoft.Extensions.DependencyInjection;
using NoteTaker.Models;
using NoteTaker.ViewModels;

namespace NoteTaker;

public partial class MainPage : ContentPage
{
	private readonly MainViewModel _viewModel;

	public MainPage()
	{
		InitializeComponent();

		_viewModel = MauiProgram.Services.GetRequiredService<MainViewModel>();
		BindingContext = _viewModel;

		Loaded += MainPage_Loaded;
	}

	private async void MainPage_Loaded(object? sender, EventArgs e)
	{
		Loaded -= MainPage_Loaded;
		await _viewModel.InitializeAsync();
	}

	private void NoteDragStarting(object sender, DragStartingEventArgs e)
	{
		if (sender is not Element element)
			return;

		if (element.BindingContext is not Note note)
			return;

		e.Data.Properties["NoteId"] = note.Id;
		e.Data.Text = note.Title;
	}

	private void FolderDragOver(object sender, DragEventArgs e)
	{
	}

	private async void FolderDrop(object sender, DropEventArgs e)
	{
		if (sender is not Element element)
			return;

		if (element.BindingContext is not Folder folder)
			return;

		if (!e.Data.Properties.TryGetValue("NoteId", out var rawValue))
			return;

		int noteId = rawValue switch
		{
			int i => i,
			long l => (int)l,
			string s when int.TryParse(s, out var parsed) => parsed,
			_ => 0
		};

		if (noteId == 0)
			return;

		await _viewModel.MoveNoteToFolderAsync(noteId, folder.Id);
	}

	private async void DeleteButtonClicked(object sender, EventArgs e)
	{
		if (!_viewModel.CanDelete)
			return;

		bool confirm = await DisplayAlert(
				"Delete note",
				"Are you sure you want to delete the selected note?",
				"Delete",
				"Cancel");

		if (!confirm)
			return;

		await _viewModel.DeleteNoteCommand.ExecuteAsync(null);
	}

	private async void DeleteFolderButtonClicked(object sender, EventArgs e)
	{
		if (!_viewModel.CanDeleteFolder || _viewModel.SelectedFolder is null)
			return;

		string folderName = _viewModel.SelectedFolder.Name;

		bool confirm = await DisplayAlert(
				"Delete folder",
				$"Delete folder '{folderName}'?\n\nIts notes will be moved to another folder.",
				"Delete",
				"Cancel");

		if (!confirm)
			return;

		await _viewModel.DeleteFolderCommand.ExecuteAsync(null);
	}
}