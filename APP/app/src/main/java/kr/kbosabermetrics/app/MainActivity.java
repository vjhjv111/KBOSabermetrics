package kr.kbosabermetrics.app;

import android.annotation.SuppressLint;
import android.app.Activity;
import android.app.AlertDialog;
import android.content.ActivityNotFoundException;
import android.content.Intent;
import android.graphics.Bitmap;
import android.graphics.Insets;
import android.net.Uri;
import android.net.http.SslError;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.Message;
import android.view.View;
import android.view.WindowInsets;
import android.view.WindowInsetsController;
import android.webkit.CookieManager;
import android.webkit.PermissionRequest;
import android.webkit.RenderProcessGoneDetail;
import android.webkit.SslErrorHandler;
import android.webkit.WebBackForwardList;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.FrameLayout;
import android.widget.PopupMenu;
import android.widget.ProgressBar;
import android.widget.TextView;
import android.widget.Toast;
import android.window.OnBackInvokedCallback;
import android.window.OnBackInvokedDispatcher;

import java.io.ByteArrayInputStream;
import java.util.Collections;
import java.util.HashSet;
import java.util.Set;

public final class MainActivity extends Activity {
    private static final String STATE_URL = "trustedPageUrl";
    private final UrlPolicy urls = new UrlPolicy(BuildConfig.SITE_BASE_URL);
    private final Handler mainHandler = new Handler(Looper.getMainLooper());
    private final Set<WebView> popupViews = new HashSet<>();
    private WebView webView;
    private FrameLayout container;
    private View errorPanel;
    private TextView errorMessage;
    private ProgressBar progress;
    private String lastTrustedUrl;
    private boolean pageFailed;
    private boolean backCallbackRegistered;
    private OnBackInvokedCallback backCallback;
    private AlertDialog externalDialog;
    private View fullscreenView;
    private WebChromeClient.CustomViewCallback fullscreenCallback;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_main);
        applySystemInsets();
        container = findViewById(R.id.web_container);
        errorPanel = findViewById(R.id.error_panel);
        errorMessage = findViewById(R.id.error_message);
        progress = findViewById(R.id.loading_progress);
        if (Build.VERSION.SDK_INT >= 33) backCallback = this::handleModernBack;

        findViewById(R.id.home_button).setOnClickListener(view -> loadTrusted(urls.baseUrl()));
        findViewById(R.id.refresh_button).setOnClickListener(view -> retry());
        findViewById(R.id.retry_button).setOnClickListener(view -> retry());
        findViewById(R.id.more_button).setOnClickListener(view -> {
            PopupMenu menu = new PopupMenu(this, view);
            menu.getMenu().add(R.string.open_browser).setOnMenuItemClickListener(item -> {
                openBrowser(currentTrustedUrl());
                return true;
            });
            menu.show();
        });

        createWebView();
        String restored = savedInstanceState == null ? null : savedInstanceState.getString(STATE_URL);
        // Restore only a validated URL, never opaque WebView state or an incoming intent URL.
        loadTrusted(urls.isInternal(restored) ? restored : urls.baseUrl());
    }

    @SuppressLint("SetJavaScriptEnabled")
    @SuppressWarnings("deprecation")
    private void configureWebView(WebView view, boolean mainView) {
        WebSettings settings = view.getSettings();
        // The site's charts and WebGL game need JavaScript; no native JS bridge is exposed.
        settings.setJavaScriptEnabled(mainView);
        settings.setDomStorageEnabled(mainView);
        settings.setAllowFileAccess(false);
        settings.setAllowContentAccess(false);
        settings.setAllowFileAccessFromFileURLs(false);
        settings.setAllowUniversalAccessFromFileURLs(false);
        settings.setMixedContentMode(WebSettings.MIXED_CONTENT_NEVER_ALLOW);
        settings.setSafeBrowsingEnabled(true);
        settings.setJavaScriptCanOpenWindowsAutomatically(false);
        settings.setSupportMultipleWindows(mainView);
        settings.setMediaPlaybackRequiresUserGesture(true);
        settings.setUseWideViewPort(true);
        settings.setLoadWithOverviewMode(true);
        settings.setBuiltInZoomControls(true);
        settings.setDisplayZoomControls(false);
        CookieManager.getInstance().setAcceptThirdPartyCookies(view, false);
    }

    private void createWebView() {
        webView = new WebView(this);
        configureWebView(webView, true);
        WebView.setWebContentsDebuggingEnabled(BuildConfig.DEBUG);
        webView.setBackgroundColor(getColor(R.color.surface));
        webView.setWebViewClient(new SiteClient());
        webView.setWebChromeClient(new SiteChromeClient());
        webView.setDownloadListener((url, userAgent, contentDisposition, mimeType, contentLength) ->
                new AlertDialog.Builder(this)
                        .setTitle(R.string.download_title)
                        .setMessage(R.string.download_message)
                        .setNegativeButton(R.string.cancel, null)
                        .setPositiveButton(R.string.open_browser, (dialog, which) -> openBrowser(currentTrustedUrl()))
                        .show());
        container.addView(webView, 0, new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT));
    }

    private void loadTrusted(String url) {
        String destination = urls.isInternal(url) ? url : urls.baseUrl();
        if (webView == null) createWebView();
        lastTrustedUrl = destination;
        prepareNavigation();
        webView.loadUrl(destination);
    }

    private void retry() {
        if (webView == null || pageFailed) {
            loadTrusted(currentTrustedUrl());
        } else {
            startLoading();
            webView.reload();
        }
    }

    private String currentTrustedUrl() {
        String current = webView == null ? null : webView.getUrl();
        if (urls.isInternal(current)) return current;
        return urls.isInternal(lastTrustedUrl) ? lastTrustedUrl : urls.baseUrl();
    }

    private void startLoading() {
        prepareNavigation();
        progress.setProgress(0);
        progress.setVisibility(View.VISIBLE);
    }

    private void prepareNavigation() {
        pageFailed = false;
        errorPanel.setVisibility(View.GONE);
        if (webView != null) webView.setVisibility(View.VISIBLE);
    }

    private void showError(String message) {
        if (isFinishing() || isDestroyed()) return;
        hideFullscreen();
        pageFailed = true;
        progress.setVisibility(View.INVISIBLE);
        errorMessage.setText(message);
        errorPanel.setVisibility(View.VISIBLE);
        if (webView != null) webView.setVisibility(View.INVISIBLE);
        updateBackHandling();
    }

    private boolean routeNavigation(String url, boolean mainFrame) {
        if (urls.isInternal(url)) return false;
        if (mainFrame) {
            if (urls.isWebLink(url)) confirmExternalLink(url);
            else Toast.makeText(this, R.string.unsupported_link, Toast.LENGTH_SHORT).show();
        }
        return true;
    }

    private void confirmExternalLink(String url) {
        if (!urls.isWebLink(url) || isFinishing() || isDestroyed()) return;
        if (externalDialog != null && externalDialog.isShowing()) return;
        externalDialog = new AlertDialog.Builder(this)
                .setTitle(R.string.external_link_title)
                .setMessage(getString(R.string.external_link_message, urls.displayOrigin(url)))
                .setNegativeButton(R.string.cancel, null)
                .setPositiveButton(R.string.external_link_open, (dialog, which) -> openBrowser(url))
                .create();
        externalDialog.show();
    }

    private void openBrowser(String url) {
        if (!urls.isWebLink(url)) return;
        Intent intent = new Intent(Intent.ACTION_VIEW, Uri.parse(url));
        intent.addCategory(Intent.CATEGORY_BROWSABLE);
        try {
            startActivity(intent);
        } catch (ActivityNotFoundException | SecurityException exception) {
            Toast.makeText(this, R.string.browser_unavailable, Toast.LENGTH_LONG).show();
        }
    }

    private boolean canNavigateBack() {
        if (webView == null || !webView.canGoBack()) return false;
        WebBackForwardList history = webView.copyBackForwardList();
        int previous = history.getCurrentIndex() - 1;
        return previous >= 0 && urls.isInternal(history.getItemAtIndex(previous).getUrl());
    }

    private void navigateBack() {
        // Hash routes are same-document history; they do not emit page start/finish callbacks.
        prepareNavigation();
        webView.goBack();
        updateBackHandling();
    }

    private void updateBackHandling() {
        if (Build.VERSION.SDK_INT < 33 || backCallback == null) return;
        boolean canHandle = fullscreenView != null || canNavigateBack();
        if (canHandle && !backCallbackRegistered) {
            getOnBackInvokedDispatcher().registerOnBackInvokedCallback(
                    OnBackInvokedDispatcher.PRIORITY_DEFAULT, backCallback);
            backCallbackRegistered = true;
        } else if (!canHandle && backCallbackRegistered) {
            getOnBackInvokedDispatcher().unregisterOnBackInvokedCallback(backCallback);
            backCallbackRegistered = false;
        }
    }

    private void handleModernBack() {
        if (fullscreenView != null) hideFullscreen();
        else if (canNavigateBack()) navigateBack();
        else {
            updateBackHandling();
            finish();
        }
    }

    @Override
    @SuppressLint("GestureBackNavigation")
    @SuppressWarnings("deprecation")
    public void onBackPressed() {
        // API 26-32 fallback only. API 33+ uses the registered platform callback above;
        // its root callback is removed so Android can perform the predictive exit animation.
        if (fullscreenView != null) hideFullscreen();
        else if (canNavigateBack()) navigateBack();
        else super.onBackPressed();
    }

    private void hideFullscreen() {
        if (fullscreenView == null) return;
        container.removeView(fullscreenView);
        fullscreenView = null;
        findViewById(R.id.toolbar).setVisibility(View.VISIBLE);
        if (webView != null) webView.setVisibility(pageFailed ? View.INVISIBLE : View.VISIBLE);
        progress.setVisibility(pageFailed || progress.getProgress() == 100 ? View.INVISIBLE : View.VISIBLE);
        WebChromeClient.CustomViewCallback callback = fullscreenCallback;
        fullscreenCallback = null;
        if (callback != null) callback.onCustomViewHidden();
        updateBackHandling();
    }

    @SuppressWarnings("deprecation")
    private void applySystemInsets() {
        View root = findViewById(R.id.root);
        if (Build.VERSION.SDK_INT >= 30) {
            getWindow().setDecorFitsSystemWindows(false);
            WindowInsetsController controller = getWindow().getInsetsController();
            if (controller != null) controller.setSystemBarsAppearance(
                    WindowInsetsController.APPEARANCE_LIGHT_STATUS_BARS |
                            WindowInsetsController.APPEARANCE_LIGHT_NAVIGATION_BARS,
                    WindowInsetsController.APPEARANCE_LIGHT_STATUS_BARS |
                            WindowInsetsController.APPEARANCE_LIGHT_NAVIGATION_BARS);
            root.setOnApplyWindowInsetsListener((view, insets) -> {
                int nativeTypes = WindowInsets.Type.systemBars() | WindowInsets.Type.displayCutout();
                Insets bars = insets.getInsets(nativeTypes);
                view.setPadding(bars.left, bars.top, bars.right, bars.bottom);
                // Keep IME notifications while zeroing bars already handled by the native container.
                return new WindowInsets.Builder(insets).setInsets(nativeTypes, Insets.NONE).build();
            });
        } else {
            // API 26–29 uses the platform's decor fitting and adjustResize for bars and keyboard.
            getWindow().getDecorView().setSystemUiVisibility(
                    View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR | View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR);
        }
        root.requestApplyInsets();
    }

    @Override
    protected void onSaveInstanceState(Bundle outState) {
        outState.putString(STATE_URL, currentTrustedUrl());
        super.onSaveInstanceState(outState);
    }

    @Override
    protected void onResume() {
        super.onResume();
        if (webView != null) webView.onResume();
    }

    @Override
    protected void onPause() {
        if (webView != null) webView.onPause();
        super.onPause();
    }

    @Override
    protected void onDestroy() {
        hideFullscreen();
        if (Build.VERSION.SDK_INT >= 33 && backCallbackRegistered) {
            getOnBackInvokedDispatcher().unregisterOnBackInvokedCallback(backCallback);
        }
        if (externalDialog != null) externalDialog.dismiss();
        for (WebView popup : popupViews) popup.destroy();
        popupViews.clear();
        if (webView != null) {
            container.removeView(webView);
            webView.stopLoading();
            webView.destroy();
            webView = null;
        }
        super.onDestroy();
    }

    private final class SiteClient extends WebViewClient {
        @Override
        public WebResourceResponse shouldInterceptRequest(WebView view, WebResourceRequest request) {
            // URL overrides are not called for every navigation (for example, POST requests).
            if (request.isForMainFrame() && !urls.isInternal(request.getUrl().toString())) {
                return new WebResourceResponse("text/plain", "UTF-8", 403, "Forbidden",
                        Collections.emptyMap(), new ByteArrayInputStream(new byte[0]));
            }
            return null;
        }

        @Override
        public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
            return routeNavigation(request.getUrl().toString(), request.isForMainFrame());
        }

        @Override
        public void onPageStarted(WebView view, String url, Bitmap favicon) {
            if (!urls.isInternal(url)) {
                view.stopLoading();
                showError(getString(R.string.unsupported_link));
                return;
            }
            lastTrustedUrl = url;
            startLoading();
            updateBackHandling();
        }

        @Override
        public void doUpdateVisitedHistory(WebView view, String url, boolean isReload) {
            if (urls.isInternal(url)) lastTrustedUrl = url;
            updateBackHandling();
        }

        @Override
        public void onPageFinished(WebView view, String url) {
            if (!pageFailed) progress.setVisibility(View.INVISIBLE);
            updateBackHandling();
        }

        @Override
        public void onReceivedError(WebView view, WebResourceRequest request, WebResourceError error) {
            if (request.isForMainFrame()) showError(getString(R.string.load_error_message));
        }

        @Override
        public void onReceivedHttpError(WebView view, WebResourceRequest request, WebResourceResponse response) {
            if (request.isForMainFrame() && response.getStatusCode() >= 400) {
                showError(getString(R.string.http_error_message, response.getStatusCode()));
            }
        }

        @Override
        public void onReceivedSslError(WebView view, SslErrorHandler handler, SslError error) {
            handler.cancel();
            if (error.getUrl().equals(lastTrustedUrl) || error.getUrl().equals(view.getUrl())) {
                showError(getString(R.string.ssl_error_message));
            }
        }

        @Override
        public boolean onRenderProcessGone(WebView view, RenderProcessGoneDetail detail) {
            container.removeView(view);
            view.destroy();
            if (view == webView) webView = null;
            showError(getString(R.string.renderer_error_message));
            return true;
        }
    }

    private final class SiteChromeClient extends WebChromeClient {
        @Override
        public void onProgressChanged(WebView view, int newProgress) {
            if (!pageFailed) {
                progress.setProgress(newProgress);
                progress.setVisibility(fullscreenView != null ? View.GONE :
                        newProgress == 100 ? View.INVISIBLE : View.VISIBLE);
            }
        }

        @Override
        public void onPermissionRequest(PermissionRequest request) {
            request.deny();
        }

        @Override
        public void onShowCustomView(View view, CustomViewCallback callback) {
            if (fullscreenView != null || pageFailed || webView == null || !urls.isInternal(webView.getUrl())) {
                callback.onCustomViewHidden();
                return;
            }
            fullscreenView = view;
            fullscreenCallback = callback;
            findViewById(R.id.toolbar).setVisibility(View.GONE);
            progress.setVisibility(View.GONE);
            webView.setVisibility(View.INVISIBLE);
            container.addView(view, new FrameLayout.LayoutParams(
                    FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT));
            updateBackHandling();
        }

        @Override
        public void onHideCustomView() {
            hideFullscreen();
        }

        @Override
        public boolean onCreateWindow(WebView view, boolean isDialog, boolean isUserGesture, Message resultMsg) {
            if (!isUserGesture || !popupViews.isEmpty()) return false;
            // A temporary, script-disabled view captures target=_blank URLs without displaying them.
            WebView popup = new WebView(MainActivity.this);
            configureWebView(popup, false);
            popupViews.add(popup);
            mainHandler.postDelayed(() -> {
                if (popupViews.remove(popup)) popup.destroy();
            }, 15000);
            popup.setWebViewClient(new WebViewClient() {
                private void accept(String url) {
                    if ("about:blank".equals(url) || !popupViews.remove(popup)) return;
                    if (urls.isInternal(url)) loadTrusted(url);
                    else routeNavigation(url, true);
                    popup.stopLoading();
                    mainHandler.post(popup::destroy);
                }

                @Override
                public boolean shouldOverrideUrlLoading(WebView child, WebResourceRequest request) {
                    accept(request.getUrl().toString());
                    return true;
                }

                @Override
                public WebResourceResponse shouldInterceptRequest(WebView child, WebResourceRequest request) {
                    if (request.isForMainFrame()) {
                        String url = request.getUrl().toString();
                        mainHandler.post(() -> accept(url));
                    }
                    return new WebResourceResponse("text/plain", "UTF-8", 403, "Forbidden",
                            Collections.emptyMap(), new ByteArrayInputStream(new byte[0]));
                }

                @Override
                public void onPageStarted(WebView child, String url, Bitmap favicon) {
                    accept(url);
                }

                @Override
                public void onReceivedSslError(WebView child, SslErrorHandler handler, SslError error) {
                    handler.cancel();
                }

                @Override
                public boolean onRenderProcessGone(WebView child, RenderProcessGoneDetail detail) {
                    popupViews.remove(child);
                    child.destroy();
                    return true;
                }
            });
            WebView.WebViewTransport transport = (WebView.WebViewTransport) resultMsg.obj;
            transport.setWebView(popup);
            resultMsg.sendToTarget();
            return true;
        }
    }
}
