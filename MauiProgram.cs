using FinanceManager.Maui.Services;
using Microsoft.Extensions.Logging;
using Plugin.LocalNotification;

namespace FinanceManager.Maui;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseLocalNotification()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();
		builder.Services.AddSingleton<FinanceService>(s =>
			new FinanceService(Path.Combine(FileSystem.AppDataDirectory, "finance.db3")));
		builder.Services.AddSingleton<SessionService>();
		builder.Services.AddSingleton<AuthService>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}