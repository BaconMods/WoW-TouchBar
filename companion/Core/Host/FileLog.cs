namespace WoWTouchBar.Core.Host;

/// <summary>Local log file under %LOCALAPPDATA%\WoWTouchBar\logs (capped at 1 MB, one backup). Never throws.</summary>
public sealed class FileLog
{
    public const long MaxBytes = 1_000_000;
    public static string DefaultDirectory =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WoWTouchBar", "logs");

    private readonly object _lock = new();
    public string Path { get; }
    public bool Echo { get; init; }

    public FileLog(string fileName, string? directory = null)
    {
        var dir = directory ?? DefaultDirectory;
        Directory.CreateDirectory(dir);
        Path = System.IO.Path.Combine(dir, fileName);
    }

    public void Write(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";
        if (Echo) Console.WriteLine(line);
        lock (_lock)
        {
            try
            {
                if (File.Exists(Path) && new FileInfo(Path).Length > MaxBytes) File.Move(Path, Path + ".1", overwrite: true);
                File.AppendAllText(Path, line + Environment.NewLine);
            }
            catch (IOException) { /* logging must never stop the app */ }
            catch (UnauthorizedAccessException) { }
        }
    }
}
