namespace NaverSabermetrics.Web;

public enum PlayerNationalityCategory
{
    Foreign,
    AsianQuota,
}

/// <summary>
/// 국내/외국인/아시아쿼터 선수 구분.
///
/// 기본 판별은 자동입니다: 선수 개인페이지의 "지명순위" 탭에 표시되는
/// OfficialPlayerProfiles.DraftText 문구를 보고, "아시아쿼터"가 포함되어 있으면
/// 아시아쿼터, "자유선발"이 포함되어 있으면 외국인으로 분류합니다
/// (<see cref="ClassifyByDraftText"/>). 이 값은 koreabaseball.com 공식 프로필을
/// 그대로 수집한 것이라 별도 관리 없이 항상 최신 상태를 반영합니다.
///
/// 아래 Entries 목록은 그 자동 판별을 사람이 직접 덮어쓰기 위한 예외용
/// 수동 명단입니다(예: 프로필 수집이 아직 안 됐거나 DraftText 문구가
/// 애매한 경우). 대부분의 경우 비워 둔 채로 사용하면 됩니다.
///
/// 사용법: 덮어쓸 선수가 있으면 아래 Entries 배열에 한 줄씩 추가하세요.
///   new("네일", "HT", PlayerNationalityCategory.Foreign),
/// - Name은 사이트 DB(Players.Name)에 저장된 표기와 정확히 같아야 합니다
///   (검색·조회 화면에 표시되는 이름 그대로).
/// - TeamCode는 동명이인이 있을 때만 구분용으로 채우고, 보통은 null로 둬도 됩니다.
/// </summary>
public static class PlayerNationalityRegistry
{
    public sealed record Entry(string Name, string? TeamCode, PlayerNationalityCategory Category);

    private static readonly Entry[] Entries =
    {
        // new("선수명", "팀코드 또는 null", PlayerNationalityCategory.Foreign),
        // new("선수명", "팀코드 또는 null", PlayerNationalityCategory.AsianQuota),
    };

    private static readonly ILookup<string, Entry> ByName =
        Entries.ToLookup(e => e.Name, StringComparer.Ordinal);

    public static bool HasAnyEntries => Entries.Length > 0;

    /// <summary>수동 명단(예외 덮어쓰기 전용)만 조회합니다.</summary>
    public static PlayerNationalityCategory? Lookup(string? name, string? teamCode)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var candidates = ByName[name];
        Entry? match = null;
        Entry? fallback = null;
        foreach (var entry in candidates)
        {
            fallback ??= entry;
            if (!string.IsNullOrEmpty(entry.TeamCode) &&
                string.Equals(entry.TeamCode, teamCode, StringComparison.OrdinalIgnoreCase))
            {
                match = entry;
                break;
            }
        }
        return (match ?? fallback)?.Category;
    }

    /// <summary>
    /// 선수 개인페이지의 "지명순위"(OfficialPlayerProfiles.DraftText) 문구로부터
    /// 국적 구분을 자동 판별합니다. 문구에 "아시아쿼터"가 있으면 아시아쿼터,
    /// "자유선발" 또는 "부상 대체 외국인선수"가 있으면 외국인, 그 외(국내 선수의
    /// 일반 지명 순위 표기 등)는 null(국내)입니다.
    /// </summary>
    public static PlayerNationalityCategory? ClassifyByDraftText(string? draftText)
    {
        if (string.IsNullOrEmpty(draftText)) return null;
        if (draftText.Contains("아시아쿼터", StringComparison.Ordinal)) return PlayerNationalityCategory.AsianQuota;
        if (draftText.Contains("자유선발", StringComparison.Ordinal)) return PlayerNationalityCategory.Foreign;
        if (draftText.Contains("부상 대체 외국인선수", StringComparison.Ordinal)) return PlayerNationalityCategory.Foreign;
        return null;
    }

    /// <summary>
    /// 최종 판별: 수동 명단(예외)이 있으면 그것을 우선하고, 없으면 DraftText
    /// 자동 판별 결과를 사용합니다.
    /// </summary>
    public static PlayerNationalityCategory? Resolve(string? name, string? teamCode, string? draftText) =>
        Lookup(name, teamCode) ?? ClassifyByDraftText(draftText);
}
