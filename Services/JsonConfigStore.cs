using System.Text.Json;
using ShutdownManager.Models;

namespace ShutdownManager.Services;

public sealed class JsonConfigStore
{
    private readonly Logger _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public JsonConfigStore(Logger logger)
    {
        _logger = logger;
        ConfigDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScheduledShutdownManager");
        ConfigPath = Path.Combine(ConfigDirectory, "config.json");
    }

    public string ConfigDirectory { get; }
    public string ConfigPath { get; }

    public AppConfig Load()
    {
        Directory.CreateDirectory(ConfigDirectory);
        if (!File.Exists(ConfigPath))
        {
            _logger.Info("No configuration file found. Creating default configuration.");
            return new AppConfig();
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            var config = JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions);
            if (config is null)
                throw new JsonException("The configuration file contained an empty JSON document.");

            config.Settings ??= new AppSettings();
            config.Schedules ??= new List<ScheduleItem>();
            config.History ??= new List<ShutdownHistoryEntry>();
            _logger.Info("Configuration loaded successfully.");
            return config;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            _logger.Error("Configuration could not be read. Defaults will be used.", exception);
            TryBackupCorruptConfig();
            return new AppConfig();
        }
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(ConfigDirectory);
        var temporaryPath = ConfigPath + ".tmp";
        var json = JsonSerializer.Serialize(config, _jsonOptions);

        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, ConfigPath, overwrite: true);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    private void TryBackupCorruptConfig()
    {
        try
        {
            if (!File.Exists(ConfigPath))
                return;

            var backupPath = ConfigPath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Copy(ConfigPath, backupPath, overwrite: false);
            _logger.Warning($"The unreadable configuration was backed up to '{backupPath}'.");
        }
        catch (Exception exception)
        {
            _logger.Error("Could not back up the unreadable configuration file.", exception);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }
}
