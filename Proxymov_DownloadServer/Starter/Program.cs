using System.Diagnostics;

string DownloadsPath = Path.Combine(Directory.GetCurrentDirectory(), "Updates");
string DestPath = Directory.GetCurrentDirectory();

Console.WriteLine("===================\n\nInstalliere Updates...");
Console.WriteLine($"Von: {DownloadsPath}\nNach: {DestPath}");
await Task.Delay(2500);

DirectoryInfo? folder = new(DownloadsPath);

if (folder.Exists)
{
    FileInfo[] files = folder.GetFiles("*", SearchOption.TopDirectoryOnly);
    files.ToList().ForEach(f =>
    {
        if (f.Name != "Starter.exe")
        {
            try
            {
                File.Move(Path.Combine(DownloadsPath, f.Name), Path.Combine(DestPath, f.Name), true);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                Console.ReadKey();
            }

            Console.WriteLine($"{f.Name} kopiert!");
        }
    });

    folder.GetDirectories().ToList().ForEach(f =>
    {
        try
        {
            if (Directory.Exists(Path.Combine(DestPath, f.Name)))
            {
                Directory.Delete(Path.Combine(DestPath, f.Name), true);
            }

            f.MoveTo(Path.Combine(DestPath, f.Name));
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            Console.ReadKey();
        }

        Console.WriteLine($"{f.Name} kopiert!");
    });

    Directory.Delete(DownloadsPath, true);
}
else
{
    return;
}

Console.WriteLine("\n\nUpdates installiert...Neustart...\n\n===================");
await Task.Delay(2500);

Process.Start("ProxyMov_DownloadServer.exe");