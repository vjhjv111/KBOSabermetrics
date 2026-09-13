package kr.kbosabermetrics.app;

import java.net.URI;
import java.net.URISyntaxException;

/** Navigation policy independent of Android, so the origin boundary can be unit-tested. */
final class UrlPolicy {
    private final URI base;

    UrlPolicy(String baseUrl) {
        URI parsed = parseWebUrl(baseUrl);
        if (parsed == null || !"https".equalsIgnoreCase(parsed.getScheme()) ||
                parsed.getRawQuery() != null || parsed.getRawFragment() != null) {
            throw new IllegalArgumentException("The site base URL must be an absolute HTTPS URL.");
        }
        base = parsed;
    }

    String baseUrl() {
        return base.toASCIIString();
    }

    boolean isInternal(String url) {
        URI candidate = parseWebUrl(url);
        return candidate != null && "https".equalsIgnoreCase(candidate.getScheme()) &&
                base.getHost().equalsIgnoreCase(candidate.getHost()) &&
                effectivePort(base) == effectivePort(candidate);
    }

    boolean isWebLink(String url) {
        return parseWebUrl(url) != null;
    }

    String displayOrigin(String url) {
        URI candidate = parseWebUrl(url);
        if (candidate == null) return "";
        int port = candidate.getPort();
        return candidate.getScheme().toLowerCase(java.util.Locale.ROOT) + "://" +
                candidate.getHost() + (port == -1 ? "" : ":" + port);
    }

    private static URI parseWebUrl(String url) {
        if (url == null || url.isEmpty()) return null;
        try {
            URI uri = new URI(url);
            String scheme = uri.getScheme();
            if ((!"https".equalsIgnoreCase(scheme) && !"http".equalsIgnoreCase(scheme)) ||
                    uri.isOpaque() || uri.getHost() == null || uri.getRawUserInfo() != null ||
                    uri.getPort() == 0 || uri.getPort() > 65535) return null;
            return uri;
        } catch (URISyntaxException | IllegalArgumentException exception) {
            return null;
        }
    }

    private static int effectivePort(URI uri) {
        return uri.getPort() == -1 ? ("https".equalsIgnoreCase(uri.getScheme()) ? 443 : 80) : uri.getPort();
    }
}
