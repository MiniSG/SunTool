using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SunTool.Core.Logging;

namespace SunTool.Core.Settings;

public sealed class JsonSettingsService : ISettingsService
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 한글을 \uXXXX 로 바꾸지 않음
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();
    private readonly IAppLogger _log;
    private JsonObject _root;

    public JsonSettingsService(string filePath, IAppLogger? log = null)
    {
        FilePath = filePath;
        _log = log ?? NullLogger.Instance;
        _root = Load();
    }

    public string FilePath { get; }

    public T Get<T>(string key, T defaultValue)
    {
        lock (_gate)
        {
            if (_root.TryGetPropertyValue(key, out var node) && node is not null)
            {
                try
                {
                    var value = node.Deserialize<T>(SerializerOptions);
                    return value is null ? defaultValue : value;
                }
                catch (Exception ex)
                {
                    _log.Warn($"설정 '{key}'을(를) 읽지 못해 기본값을 사용합니다.", ex);
                }
            }

            return defaultValue;
        }
    }

    public void Set<T>(string key, T value)
    {
        lock (_gate)
        {
            _root[key] = JsonSerializer.SerializeToNode(value, SerializerOptions);
        }
    }

    public void Remove(string key)
    {
        lock (_gate)
        {
            _root.Remove(key);
        }
    }

    public void Save()
    {
        string json;
        lock (_gate)
        {
            json = _root.ToJsonString(SerializerOptions);
        }

        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 저장 도중 꺼져도 설정 파일이 깨지지 않도록 임시 파일에 쓴 뒤 교체합니다.
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, FilePath, overwrite: true);
            _log.Debug($"설정 저장: {FilePath}");
        }
        catch (Exception ex)
        {
            _log.Error("설정 파일을 저장하지 못했습니다.", ex);
        }
    }

    private JsonObject Load()
    {
        if (!File.Exists(FilePath))
        {
            return new JsonObject();
        }

        try
        {
            var node = JsonNode.Parse(File.ReadAllText(FilePath));
            return node as JsonObject ?? new JsonObject();
        }
        catch (Exception ex)
        {
            _log.Error("설정 파일이 손상되어 새로 만듭니다. 기존 파일은 .bak 으로 보관합니다.", ex);
            try
            {
                File.Copy(FilePath, FilePath + ".bak", overwrite: true);
            }
            catch
            {
                // 백업 실패는 무시
            }

            return new JsonObject();
        }
    }
}
