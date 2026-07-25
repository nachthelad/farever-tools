using FareverTranslate.Core.Configuration;

namespace FareverTranslate.Core.Diagnostics;

public sealed class AppLogger
{
    private readonly object gate = new();
    private readonly AppPaths paths;

    public AppLogger(AppPaths paths)
    {
        this.paths = paths;
    }

    public void Info(string eventName, string detail = "") =>
        Write("INFO", eventName, detail);

    public void Warn(string eventName, string detail = "") =>
        Write("WARN", eventName, detail);

    public void Error(string eventName, Exception exception) =>
        Write("ERROR", eventName, exception.GetType().Name);

    private void Write(string level, string eventName, string detail)
    {
        paths.EnsureCreated();
        string safeEvent = Sanitize(eventName);
        string safeDetail = Sanitize(detail);
        string line =
            $"{DateTimeOffset.Now:O} [{level}] {safeEvent}" +
            (safeDetail.Length == 0 ? string.Empty : $" {safeDetail}") +
            Environment.NewLine;
        lock (gate)
        {
            File.AppendAllText(paths.LogFile, line);
        }
    }

    private static string Sanitize(string value)
    {
        string singleLine = value.Replace('\r', ' ').Replace('\n', ' ');
        return singleLine.Length <= 240 ? singleLine : singleLine[..240];
    }
}

