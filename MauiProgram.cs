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
		var baseAddress = DeviceInfo.Platform == DevicePlatform.Android 
			? "http://10.0.2.2:5000" 
			: "http://localhost:5000";
		builder.Services.AddSingleton(new HttpClient { BaseAddress = new Uri(baseAddress) });
		builder.Services.AddSingleton<ApiService>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}