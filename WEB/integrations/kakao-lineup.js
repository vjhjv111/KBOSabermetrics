// Requires KBO_SITE_BASE_URL and kboHttpGet from the existing bot.
function GetKboLineup(msg) {
    var match = /^\/라인업\s+@?(.+)$/.exec(String(msg).trim());
    if (!match || !match[1].trim() || match[1].length > 30)
        return "사용법: /라인업 한화 (또는 /라인업 @한화)";
    try {
        var res = kboHttpGet(KBO_SITE_BASE_URL + "/api/bot/lineup?team=" + encodeURIComponent(match[1].trim()));
        if (res.code === 400) return "팀명을 확인해 주세요. 예: /라인업 한화";
        if (res.code === 404) return "라인업 API가 아직 배포되지 않았습니다.";
        if (res.code === 429 || res.code === 503) return "라인업 조회가 지연되고 있습니다. 잠시 후 다시 시도해 주세요.";
        if (res.code < 200 || res.code >= 300) return "라인업 조회에 실패했습니다. (서버 응답 " + res.code + ")";
        var data = JSON.parse(String(res.body));
        return typeof data.text === "string" && data.text.trim() ? data.text : "라인업 응답을 확인할 수 없습니다.";
    } catch (e) {
        return "라인업 서버에 연결하지 못했습니다. 잠시 후 다시 시도해 주세요.";
    }
}

// Add at the beginning of responseFix, before PC forwarding:
// if (/^\/라인업(?:\s|$)/.test(String(msg).trim())) {
//     new javaLang.Thread({run: function() {replier.reply(GetKboLineup(msg));}}).start();
//     return;
// }
