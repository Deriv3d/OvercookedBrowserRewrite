import UIKit
import WebKit

/// Shows the packed phone page (Game/index.html) full screen.
///
/// The page is served from the app as app://localhost/..., the same address
/// scheme Cordova apps use, so the game's saves (IndexedDB) live in this app's
/// own storage and stay there between launches and re-signs.
final class GameViewController: UIViewController, WKNavigationDelegate, WKUIDelegate, WKScriptMessageHandlerWithReply {
    static let scheme = "app"
    private static let startPage = URL(string: "app://localhost/index.html")!

    private var webView: WKWebView!

    override func loadView() {
        let config = WKWebViewConfiguration()
        let game = Bundle.main.resourceURL!.appendingPathComponent("Game", isDirectory: true)
        config.setURLSchemeHandler(BundleSchemeHandler(root: game), forURLScheme: Self.scheme)
        config.allowsInlineMediaPlayback = true
        config.mediaTypesRequiringUserActionForPlayback = []
        // "Back up saves" (port/mobile/template/saves.js) hands the file to the
        // Share sheet through this; web views have no reliable download.
        config.userContentController.addScriptMessageHandler(self, contentWorld: .page, name: "overcookedShare")
        let version = Bundle.main.infoDictionary?["CFBundleVersion"] as? String ?? "?"
        config.userContentController.addUserScript(WKUserScript(
            source: "window.OvercookedApp = { shell: \"\(version)\" };",
            injectionTime: .atDocumentStart, forMainFrameOnly: true))

        let webView = WKWebView(frame: .zero, configuration: config)
        webView.isOpaque = false
        webView.backgroundColor = .black
        webView.scrollView.backgroundColor = .black
        webView.scrollView.isScrollEnabled = false
        webView.scrollView.bounces = false
        webView.scrollView.contentInsetAdjustmentBehavior = .never
        webView.allowsLinkPreview = false
        webView.navigationDelegate = self
        webView.uiDelegate = self
        if #available(iOS 16.4, *) {
            // Lets Safari's Web Inspector on a Mac attach, for logs.
            webView.isInspectable = true
        }
        self.webView = webView
        view = webView
    }

    override func viewDidLoad() {
        super.viewDidLoad()
        webView.load(URLRequest(url: Self.startPage))
    }

    override func viewDidAppear(_ animated: Bool) {
        super.viewDidAppear(animated)
        // WebKit only gives the page gamepads while the web view is first
        // responder (WebKit bug 269292).
        webView.becomeFirstResponder()
    }

    override var prefersStatusBarHidden: Bool { true }
    override var prefersHomeIndicatorAutoHidden: Bool { true }
    override var preferredScreenEdgesDeferringSystemGestures: UIRectEdge { .all }
    override var supportedInterfaceOrientations: UIInterfaceOrientationMask { .landscape }

    // MARK: - Navigation

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        webView.becomeFirstResponder()
    }

    func webView(_ webView: WKWebView, decidePolicyFor action: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        // Anything outside the game (a link in the credits) opens in Safari.
        if let url = action.request.url, url.scheme != Self.scheme, url.scheme != "blob", url.scheme != "about" {
            UIApplication.shared.open(url)
            decisionHandler(.cancel)
            return
        }
        decisionHandler(.allow)
    }

    func webViewWebContentProcessDidTerminate(_ webView: WKWebView) {
        // iOS ended the page (usually memory). Safari reloads the tab in this
        // case; do the same instead of leaving a black screen.
        webView.load(URLRequest(url: Self.startPage))
    }

    // MARK: - alert() and confirm() (the restore asks before replacing saves)

    func webView(_ webView: WKWebView, runJavaScriptAlertPanelWithMessage message: String, initiatedByFrame frame: WKFrameInfo,
                 completionHandler: @escaping () -> Void) {
        let alert = UIAlertController(title: nil, message: message, preferredStyle: .alert)
        alert.addAction(UIAlertAction(title: "OK", style: .default) { _ in completionHandler() })
        present(alert, animated: true)
    }

    func webView(_ webView: WKWebView, runJavaScriptConfirmPanelWithMessage message: String, initiatedByFrame frame: WKFrameInfo,
                 completionHandler: @escaping (Bool) -> Void) {
        let alert = UIAlertController(title: nil, message: message, preferredStyle: .alert)
        alert.addAction(UIAlertAction(title: "Cancel", style: .cancel) { _ in completionHandler(false) })
        alert.addAction(UIAlertAction(title: "OK", style: .default) { _ in completionHandler(true) })
        present(alert, animated: true)
    }

    // MARK: - Back up saves

    /// Body: { name, text }. Replies true once the file was saved or sent,
    /// false if the Share sheet was closed.
    func userContentController(_ controller: WKUserContentController, didReceive message: WKScriptMessage,
                               replyHandler: @escaping (Any?, String?) -> Void) {
        guard let body = message.body as? [String: Any], let name = body["name"] as? String, let text = body["text"] as? String else {
            replyHandler(nil, "Backup failed: bad message from the page.")
            return
        }
        let fileName = (name as NSString).lastPathComponent
        let file = FileManager.default.temporaryDirectory.appendingPathComponent(fileName.isEmpty ? "overcooked-saves.json" : fileName)
        do {
            try Data(text.utf8).write(to: file, options: .atomic)
        } catch {
            replyHandler(nil, "Backup failed: " + error.localizedDescription)
            return
        }
        let sheet = UIActivityViewController(activityItems: [file], applicationActivities: nil)
        sheet.popoverPresentationController?.sourceView = view
        sheet.popoverPresentationController?.sourceRect = CGRect(x: view.bounds.midX, y: view.bounds.midY, width: 1, height: 1)
        var replied = false
        sheet.completionWithItemsHandler = { _, completed, _, _ in
            guard !replied else { return }
            replied = true
            replyHandler(completed, nil)
        }
        present(sheet, animated: true)
    }
}
