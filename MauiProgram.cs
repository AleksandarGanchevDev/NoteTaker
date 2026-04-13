using Microsoft.Extensions.Logging;
using NoteTaker.Services;
using NoteTaker.ViewModels;

namespace NoteTaker;

public static class MauiProgram
{
	public static IServiceProvider Services { get; private set; } = default!;

	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();

		builder
				.UseMauiApp<App>()
				.ConfigureFonts(fonts =>
				{
					fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
					fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
				});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		builder.Services.AddSingleton<NoteDatabase>();
		builder.Services.AddSingleton<MainViewModel>();

		var app = builder.Build();
		Services = app.Services;

		return app;
	}
}