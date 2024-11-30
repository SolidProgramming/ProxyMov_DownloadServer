global using ProxyMov_DownloadServer.Classes;
global using ProxyMov_DownloadServer.Enums;
global using ProxyMov_DownloadServer.Factories;
global using ProxyMov_DownloadServer.Interfaces;
global using ProxyMov_DownloadServer.Misc;
global using ProxyMov_DownloadServer.Models;
global using ProxyMov_DownloadServer.Services;
using Havit.Blazor.Components.Web;
using ProxyMov_DownloadServer.Components;
using PuppeteerSharp;
using Quartz;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using Toolbelt.Blazor.Extensions.DependencyInjection;
using Toolbelt.Blazor.I18nText;

const string hostUrl = "http://localhost:8080";

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") != "true")
{
    builder.WebHost.UseUrls(hostUrl);
}

builder.AddServiceDefaults();

// Add services to the container.
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

builder.Services.AddI18nText(_ =>
{
    _.PersistenceLevel = PersistanceLevel.PersistentCookie;
});

builder.Services.AddHttpClient<IApiService, ApiService>();

builder.Services.AddSingleton<IApiService, ApiService>();
builder.Services.AddSingleton<IConverterService, ConverterService>();
builder.Services.AddSingleton<IQuartzService, QuartzService>();
builder.Services.AddSingleton<Updater.Interfaces.IUpdateService, Updater.Services.UpdateService>();

WebApplication app = builder.Build();

SettingsModel? settings = SettingsHelper.ReadSettings<SettingsModel>();

if (settings is null || string.IsNullOrEmpty(settings.ApiUrl))
{
    app.Logger.LogError($"{DateTime.Now} | Could not find Settings.json file or settings not complete.");
    return;
}

app.Logger.LogInformation($"{DateTime.Now} | Downloading and installing chrome to: {Helper.GetBrowserPath()}");

BrowserFetcherOptions browserFetcherOptions = new() { Path = Helper.GetBrowserPath(), Browser = SupportedBrowser.Chrome };
BrowserFetcher? browserFetcher = new(browserFetcherOptions);
await browserFetcher.DownloadAsync();

IConverterService converterService = app.Services.GetRequiredService<IConverterService>();
bool converterInitSuccess = converterService.Init();

if (!converterInitSuccess)
{
    app.Logger.LogError($"{DateTime.Now} | Converter couldn't be initialized!");
    return;
}

IApiService apiService = app.Services.GetRequiredService<IApiService>();
bool apiInitSuccess = apiService.Init();

if (!apiInitSuccess)
{
    app.Logger.LogError($"{DateTime.Now} | API service couldn't be initialized!");
    return;
}

HosterModel? sto = HosterHelper.GetHosterByEnum(Hoster.STO);
HosterModel? aniworld = HosterHelper.GetHosterByEnum(Hoster.AniWorld);

DownloaderPreferencesModel? downloaderPreferences = await apiService.GetAsync<DownloaderPreferencesModel?>("getDownloaderPreferences");

WebProxy? proxy = default;

if (downloaderPreferences is not null && downloaderPreferences.UseProxy)
{
    app.Logger.LogInformation($"{DateTime.Now} | Proxy configured: {downloaderPreferences.ProxyUri}");

    proxy = ProxyFactory.CreateProxy(new ProxyAccountModel()
    {
        Uri = downloaderPreferences.ProxyUri,
        Username = downloaderPreferences.ProxyUsername,
        Password = downloaderPreferences.ProxyPassword
    });
}

(bool success, string? ipv4) = await new HttpClient().GetIPv4();
if (!success)
{
    app.Logger.LogError($"{DateTime.Now} | HttpClient could not retrieve WAN IP Address.");
    return;
}

app.Logger.LogInformation($"{DateTime.Now} | Your WAN IP is: {ipv4}");

app.Logger.LogInformation($"{DateTime.Now} | Checking if Hosters are reachable...");

bool hosterReachableSTO = await HosterHelper.HosterReachable(sto, proxy);

if (!hosterReachableSTO)
{
    app.Logger.LogError($"{DateTime.Now} | Hoster: {sto.Host} not reachable. Maybe there is a captcha you need to solve.");
    return;
}

bool hosterReachableAniworld = await HosterHelper.HosterReachable(aniworld, proxy);

if (!hosterReachableAniworld)
{
    app.Logger.LogError($"{DateTime.Now} | Hoster: {aniworld.Host} not reachable. Maybe there is a captcha you need to solve.");
    return;
}

app.Logger.LogInformation($"{DateTime.Now} | Initializing Cronjob and HttpClients...");
await CronJob.InitAsync(proxy);

IQuartzService quartz = app.Services.GetRequiredService<IQuartzService>();
await quartz.Init();

if (downloaderPreferences is null)
{
    await quartz.CreateJob(15);
}
else
{
    if (downloaderPreferences.AutoStart)
    {
        await quartz.CreateJob(downloaderPreferences.Interval);
    }
}

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
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

if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") != "true")
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