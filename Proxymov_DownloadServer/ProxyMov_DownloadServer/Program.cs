global using ProxyMov_DownloadServer.Classes;
global using ProxyMov_DownloadServer.Enums;
global using ProxyMov_DownloadServer.Factories;
global using ProxyMov_DownloadServer.Interfaces;
global using ProxyMov_DownloadServer.Misc;
global using ProxyMov_DownloadServer.Models;
global using ProxyMov_DownloadServer.Services;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using Havit.Blazor.Components.Web;
using ProxyMov_DownloadServer.Components;
using ProxyMov_DownloadServer.ServiceDefaults;
using PuppeteerSharp;
using Quartz;
using Toolbelt.Blazor.Extensions.DependencyInjection;
using Toolbelt.Blazor.I18nText;
using Updater.Interfaces;
using Updater.Services;

const string hostUrl = "http://localhost:8080";
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") != "true")
{
    builder.WebHost.UseUrls(hostUrl);
}

builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHxServices();
builder.Services.AddHxMessenger();

builder.Services.AddHsts(_ =>
{
    _.Preload = true;
    _.IncludeSubDomains = true;
});

builder.Services.AddQuartz();

builder.Services.AddQuartzHostedService(_ =>
{
    _.WaitForJobsToComplete = true;
    _.AwaitApplicationStarted = true;
});

builder.Services.AddI18nText(_ => { _.PersistenceLevel = PersistanceLevel.PersistentCookie; });
builder.Services.ConfigureHttpClientDefaults(httpClientBuilder =>
{
    httpClientBuilder.ConfigureHttpClient(client =>
    {
        client.Timeout = Timeout.InfiniteTimeSpan;
    });

    httpClientBuilder.AddResilienceHandler("default", (pipeline, context) =>
    {
        ILoggerFactory loggerFactory = context.ServiceProvider.GetRequiredService<ILoggerFactory>();
        HttpResiliencePipelineConfigurator.Configure(pipeline, loggerFactory, context.BuilderName);
    });
});

builder.Services.AddHttpClient<IApiService, ApiService>();

builder.Services.AddSingleton<IApiService, ApiService>();
builder.Services.AddSingleton<IConverterService, ConverterService>();
builder.Services.AddSingleton<ProxyMov_DownloadServer.Interfaces.IHttpClientFactory, HttpClientFactory>();
builder.Services.AddSingleton<IQuartzService, QuartzService>();
builder.Services.AddSingleton<IUpdateService, UpdateService>();
builder.Services.AddTransient<CronJob>();
builder.Services.AddSingleton<IStreamingPortalServiceFactory>(_ =>
{
    StreamingPortalServiceFactory streamingPortalServiceFactory = new();
    streamingPortalServiceFactory.AddService(StreamingPortal.AniWorld, _);
    streamingPortalServiceFactory.AddService(StreamingPortal.STO, _);

    return streamingPortalServiceFactory;
});

WebApplication app = builder.Build();

SettingsModel? settings = SettingsHelper.ReadSettings<SettingsModel>();

if (settings is null || string.IsNullOrEmpty(settings.ApiUrl))
{
    app.Logger.LogError($"{DateTime.UtcNow.ToLocalTime()} | Could not find Settings.json file or settings not complete.");
    return;
}

app.Logger.LogInformation($"{DateTime.UtcNow.ToLocalTime()} | Downloading and installing chrome to: {Helper.GetBrowserPath()}");

BrowserFetcherOptions browserFetcherOptions =
    new() { Path = Helper.GetBrowserPath(), Browser = SupportedBrowser.Chrome };
BrowserFetcher? browserFetcher = new(browserFetcherOptions);
await browserFetcher.DownloadAsync();

IConverterService converterService = app.Services.GetRequiredService<IConverterService>();
bool converterInitSuccess = converterService.Init();

if (!converterInitSuccess)
{
    app.Logger.LogError($"{DateTime.UtcNow.ToLocalTime()} | Converter couldn't be initialized!");
    return;
}

IApiService apiService = app.Services.GetRequiredService<IApiService>();
bool apiInitSuccess = apiService.Init();

if (!apiInitSuccess)
{
    app.Logger.LogError($"{DateTime.UtcNow.ToLocalTime()} | API service couldn't be initialized!");
    return;
}

ProxyMov_DownloadServer.Interfaces.IHttpClientFactory httpClientFactory = app.Services.GetRequiredService<ProxyMov_DownloadServer.Interfaces.IHttpClientFactory>();
IStreamingPortalServiceFactory streamingPortalServiceFactory = app.Services.GetRequiredService<IStreamingPortalServiceFactory>();

DownloaderPreferencesModel? downloaderPreferences =
    await apiService.GetAsync<DownloaderPreferencesModel?>("getDownloaderPreferences");

WebProxy? proxy = null;

if (downloaderPreferences is not null && downloaderPreferences.UseProxy)
{
    app.Logger.LogInformation($"{DateTime.UtcNow.ToLocalTime()} | Proxy configured: {downloaderPreferences.ProxyUri}");

    bool proxyCreated = ProxyFactory.CreateProxy(new ProxyAccountModel
    {
        Uri = downloaderPreferences.ProxyUri,
        Username = downloaderPreferences.ProxyUsername,
        Password = downloaderPreferences.ProxyPassword
    }, out proxy);

    if (!proxyCreated)
    {
        app.Logger.LogError($"{DateTime.UtcNow.ToLocalTime()} | Configured Proxy could not be created.");

        await Task.Delay(10000);

        return;
    }
}

(bool success, string? ipv4) = await new HttpClient().GetIPv4();
if (!success)
{
    app.Logger.LogError($"{DateTime.UtcNow.ToLocalTime()} | HttpClient could not retrieve WAN IP Address.");

    await Task.Delay(10000);

    return;
}

app.Logger.LogInformation($"{DateTime.UtcNow.ToLocalTime()} | Your WAN IP is: {ipv4}");

app.Logger.LogInformation($"{DateTime.UtcNow.ToLocalTime()} | Initializing Cronjob and HttpClients...");

IStreamingPortalService stoService = streamingPortalServiceFactory.GetService(StreamingPortal.STO);
IStreamingPortalService aniWorldService = streamingPortalServiceFactory.GetService(StreamingPortal.AniWorld);

bool stoInitialized = await stoService.InitAsync(proxy);
bool aniWorldInitialized = await aniWorldService.InitAsync(proxy);

if (!stoInitialized || !aniWorldInitialized)
{
    app.Logger.LogError($"{DateTime.UtcNow.ToLocalTime()} | Streaming portal services couldn't be initialized!");
    return;
}

IQuartzService quartz = app.Services.GetRequiredService<IQuartzService>();
await quartz.Init();

if (downloaderPreferences is null)
{
    await quartz.CreateJob(15);
}
else if (downloaderPreferences.AutoStart)
{
    await quartz.CreateJob(downloaderPreferences.Interval);
}

app.MapDefaultEndpoints();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseHsts();
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
    Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") != "true")
{
    OpenBrowser(hostUrl);
}

app.Run();

static void OpenBrowser(string url)
{
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
        if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") != "true")
        {
            Process.Start("xdg-open", url);
        }
    }
    else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
    {
        Process.Start("open", url);
    }
}
