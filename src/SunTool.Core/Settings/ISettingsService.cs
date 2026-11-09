namespace SunTool.Core.Settings;

/// <summary>키-값 형태의 JSON 설정 저장소. 각 모듈은 자기 이름을 키 접두어로 씁니다.</summary>
public interface ISettingsService
{
    string FilePath { get; }

    T Get<T>(string key, T defaultValue);

    void Set<T>(string key, T value);

    void Remove(string key);

    void Save();
}
