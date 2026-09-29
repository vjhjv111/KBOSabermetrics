// Uses the original bot's KBO_SITE_BASE_URL and kboHttpGet.
function GetKboStarters(msg) {
    var command = String(msg).trim();
    if (command !== "/선발" && command !== "/내일선발") return "사용법: /선발 또는 /내일선발";
    try {
        var offset = command === "/내일선발" ? 1 : 0;
        var res = kboHttpGet(KBO_SITE_BASE_URL + "/api/bot/starters?dayOffset=" + offset);
        if (res.code === 404) return "선발 API가 아직 배포되지 않았습니다. 서버 업데이트 후 다시 시도해 주세요.";
        if (res.code === 429 || res.code === 503) return "선발 조회가 잠시 지연되고 있습니다. 잠시 후 다시 시도해 주세요.";
        if (res.code < 200 || res.code >= 300) return "선발 정보를 가져오지 못했습니다. (서버 응답 " + res.code + ")";
        var data = JSON.parse(String(res.body));
        if (typeof data.text !== "string" || !data.text.trim()) return "선발 응답을 확인할 수 없습니다. 잠시 후 다시 시도해 주세요.";
        return data.text;
    } catch (e) {
        return "선발 서버에 연결하지 못했습니다. 잠시 후 다시 시도해 주세요.";
    }
}

// Insert at the start of responseFix, before forwarding commands to the PC:
// if (/^\/(?:선발|내일선발)(?:\s|$)/.test(String(msg).trim())) {
//     new javaLang.Thread({run: function() {replier.reply(GetKboStarters(msg));}}).start();
//     return;
// }
