package kr.kbosabermetrics.app;

import org.junit.Test;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertThrows;
import static org.junit.Assert.assertTrue;

public class UrlPolicyTest {
    private final UrlPolicy urls = new UrlPolicy("https://kbosabermetrics.onrender.com/");

    @Test
    public void sameHttpsOriginAllowsPagesQueriesAndGameFragment() {
        assertTrue(urls.isInternal("https://kbosabermetrics.onrender.com/"));
        assertTrue(urls.isInternal("https://kbosabermetrics.onrender.com/?year=2026#diamond"));
        assertTrue(urls.isInternal("https://kbosabermetrics.onrender.com/player/62404"));
        assertTrue(urls.isInternal("HTTPS://KBOSABERMETRICS.ONRENDER.COM:443/"));
    }

    @Test
    public void originIncludesExactHostSchemeAndEffectivePort() {
        String[] otherOrigins = {
                "http://kbosabermetrics.onrender.com/",
                "https://kbosabermetrics.onrender.com:444/",
                "https://kbosabermetrics.onrender.com.evil.example/",
                "https://evil.kbosabermetrics.onrender.com/",
                "https://kbosabermetrics.onrender.com./",
                "https://evil.example/?next=https://kbosabermetrics.onrender.com/"
        };
        for (String url : otherOrigins) {
            assertFalse(url, urls.isInternal(url));
            assertTrue(url, urls.isWebLink(url));
        }
    }

    @Test
    public void unsafeSchemesMalformedUrlsAndCredentialsAreBlockedEverywhere() {
        String[] rejected = {
                null, "", "/player/62404", "//kbosabermetrics.onrender.com/",
                "javascript:alert(1)", "javascript://kbosabermetrics.onrender.com/",
                "file:///sdcard/example.html", "content://records/1", "intent://records/#Intent;end",
                "data:text/html,<h1>unsafe</h1>", "about:blank", "blob:https://kbosabermetrics.onrender.com/123",
                "https://kbosabermetrics.onrender.com@evil.example/",
                "https://user:secret@kbosabermetrics.onrender.com/",
                "https://evil.example\\@kbosabermetrics.onrender.com/",
                "https://kbosabermetrics.onrender.com\n.evil.example/",
                "https://kbosabermetrics%2eonrender.com/", "https://kbosabermetrics.onrender.com:0/",
                "https://kbosabermetrics.onrender.com:65536/", "https:///missing-host"
        };
        for (String url : rejected) {
            assertFalse(String.valueOf(url), urls.isInternal(url));
            assertFalse(String.valueOf(url), urls.isWebLink(url));
        }
    }

    @Test
    public void customHttpsOriginSupportsExplicitPort() {
        UrlPolicy custom = new UrlPolicy("https://records.example:8443/start/");
        assertTrue(custom.isInternal("https://records.example:8443/#comparison"));
        assertFalse(custom.isInternal("https://records.example/"));
        assertFalse(custom.isInternal(urls.baseUrl()));
        assertEquals("https://records.example:8443", custom.displayOrigin("https://records.example:8443/?private=1"));
    }

    @Test
    public void baseUrlRequiresHttpsWithoutCredentialsQueryOrFragment() {
        for (String invalid : new String[]{"http://records.example/", "file:///tmp/index.html",
                "https://user@records.example/", "https://records.example/?token=secret",
                "https://records.example/#diamond", "not a url"}) {
            assertThrows(invalid, IllegalArgumentException.class, () -> new UrlPolicy(invalid));
        }
    }
}
