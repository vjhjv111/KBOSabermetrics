// Add to AIbotUDPToPC.js. Uses its existing KBO_SITE_BASE_URL and kboHttpGet helper.
function GetKboGames(msg) {
    var parts = String(msg).trim().split(/\s+/);
    if (parts.length > 2 || (parts.length === 2 && !/^\d{4}-\d{2}-\d{2}$/.test(parts[1]))) {
        return "사용법: /경기 또는 /경기 2026-09-29";
    }
    try {
        // Omit date for today: the server uses Korea time regardless of the phone timezone.
        var url = KBO_SITE_BASE_URL + "/api/bot/games";
        if (parts.length === 2) url += "?date=" + encodeURIComponent(parts[1]);
        var res = kboHttpGet(url);
        if (res.code === 404) return "경기 API가 아직 배포되지 않았습니다. 서버 업데이트 후 다시 시도해 주세요.";
        if (res.code === 400) return "날짜를 확인해 주세요. 사용법: /경기 2026-09-29";
        if (res.code === 429 || res.code === 503) return "경기 조회가 잠시 지연되고 있습니다. 잠시 후 다시 시도해 주세요.";
        if (res.code < 200 || res.code >= 300) return "경기 정보를 가져오지 못했습니다. (서버 응답 " + res.code + ")";
        var data = JSON.parse(String(res.body));
        if (typeof data.text !== "string" || !data.text.trim()) return "경기 응답을 확인할 수 없습니다. 잠시 후 다시 시도해 주세요.";
        return data.text;
    } catch (e) {
        return "경기 서버에 연결하지 못했습니다. 잠시 후 다시 시도해 주세요.";
    }
}

// Insert this branch at the beginning of responseFix:
// if (/^\/경기(?:\s|$)/.test(String(msg).trim())) {
//     new javaLang.Thread({run: function() {replier.reply(GetKboGames(msg));}}).start();
//     return;
// }
