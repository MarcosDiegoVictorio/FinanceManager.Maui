using FinanceManager.Maui.Services;
using Microsoft.Extensions.Logging;
using Plugin.LocalNotification;

namespace FinanceManager.Maui;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		// Set EPPlus License to NonCommercial for EPPlus 8+
		OfficeOpenXml.ExcelPackage.License.SetNonCommercialPersonal("FinanceVictorios");

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

		// Configure HttpClient with loopback address for Android emulator or localhost for Windows/iOS
#if WINDOWS
			var baseAddress = "https://localhost:7268";
#elif ANDROID
		// Use HTTP + mapped loopback port for Android emulator (avoid dev-cert issues)
		var baseAddress = "http://10.0.2.2:5117";
#else
			var baseAddress = "https://localhost:7268";
#endif
		builder.Services.AddSingleton(new HttpClient { BaseAddress = new Uri(baseAddress) });

		builder.Services.AddSingleton<ApiService>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}