using System.Text.Json.Serialization;

namespace SunTool.Modules.EventLogViewer.Logic;

public enum EventDefinitionSource
{
    /// <summary>공급자 매니페스트 (최신 방식).</summary>
    Manifest,

    /// <summary>메시지 파일의 메시지 테이블 (예전 방식).</summary>
    MessageFile,
}

/// <summary>Windows 에 등록된 이벤트 정의 하나 (원본 + ID → 메시지 형식).</summary>
public sealed class EventDefinition
{
    public required string ProviderName { get; init; }

    public required int EventId { get; init; }

    /// <summary>0 = 지정 안 됨, 1 위험, 2 오류, 3 경고, 4 정보, 5 자세한 정보.</summary>
    public byte Level { get; init; }

    public string Task { get; init; } = string.Empty;

    /// <summary>메시지 형식. %1, %2 … 자리는 실제 이벤트에서 값으로 채워집니다.</summary>
    public string Description { get; init; } = string.Empty;

    public EventDefinitionSource Source { get; init; }

    [JsonIgnore]
    public EventLevelKind LevelKind => EventLevelInfo.FromLevel(Level);

    [JsonIgnore]
    public string LevelText => Level == 0 ? Loc.T("지정 안 됨") : EventLevelInfo.ToKorean(LevelKind);

    [JsonIgnore]
    public string SourceText => Source == EventDefinitionSource.Manifest ? Loc.T("매니페스트") : Loc.T("메시지 파일");

    [JsonIgnore]
    public string Summary
    {
        get
        {
            var line = string.Join(' ', Description.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return line.Length > 200 ? line[..200] + "…" : line;
        }
    }

    [JsonIgnore]
    public string Details =>
        Loc.F("원본: {0}    이벤트 ID: {1}    수준: {2}    작업 범주: {3}    출처: {4}\r\n", ProviderName, EventId, LevelText, (string.IsNullOrEmpty(Task) ? "없음" : Task), SourceText) +
        new string('─', 60) + "\r\n" +
        (string.IsNullOrWhiteSpace(Description) ? Loc.T("(설명이 등록되어 있지 않습니다)") : Description.Replace("\n", "\r\n")) +
        Loc.T("\r\n\r\n※ %1, %2 … 는 실제 이벤트가 기록될 때 서비스 이름·오류 내용 등으로 채워지는 자리입니다.");
}

public readonly record struct CatalogProgress(int Done, int Total);
