import Foundation
import WebKit

/// Serves app://localhost/<path> from the app's Game folder, streamed in 4 MB
/// pieces so the ~190 MB data file never sits in memory twice.
///
/// pack_ipa.py stores the build files uncompressed (a web view only unpacks
/// gzip it gets over the network), so nothing is decoded here.
final class BundleSchemeHandler: NSObject, WKURLSchemeHandler {
    private static let chunkSize = 4 << 20
    private static let types = [
        "html": "text/html; charset=utf-8", "js": "application/javascript", "wasm": "application/wasm",
        "json": "application/json", "css": "text/css", "png": "image/png", "jpg": "image/jpeg", "ico": "image/x-icon",
        "txt": "text/plain; charset=utf-8",
    ]

    private let root: URL
    private let reader = DispatchQueue(label: "overcooked.files", qos: .userInitiated)
    /// Tasks WebKit hasn't stopped yet. Main thread only.
    private var live = Set<ObjectIdentifier>()

    init(root: URL) {
        self.root = root.standardizedFileURL
    }

    func webView(_ webView: WKWebView, start task: WKURLSchemeTask) {
        let id = ObjectIdentifier(task)
        live.insert(id)
        guard let url = task.request.url, let file = resolve(url) else {
            let response = HTTPURLResponse(url: task.request.url ?? URL(string: "app://localhost/")!, statusCode: 404,
                                           httpVersion: "HTTP/1.1", headerFields: ["Content-Type": "text/plain"])!
            task.didReceive(response)
            task.didReceive(Data("Not found".utf8))
            task.didFinish()
            live.remove(id)
            return
        }
        reader.async { [weak self] in
            self?.stream(file, url: url, to: task, id: id)
        }
    }

    func webView(_ webView: WKWebView, stop task: WKURLSchemeTask) {
        live.remove(ObjectIdentifier(task))
    }

    /// The file for a URL path, only inside the Game folder.
    private func resolve(_ url: URL) -> URL? {
        var path = url.path
        if path.isEmpty || path == "/" { path = "/index.html" }
        let file = root.appendingPathComponent(String(path.dropFirst())).standardizedFileURL
        guard file.path.hasPrefix(root.path + "/") else { return nil }
        var isFolder: ObjCBool = false
        guard FileManager.default.fileExists(atPath: file.path, isDirectory: &isFolder), !isFolder.boolValue else { return nil }
        return file
    }

    private func stream(_ file: URL, url: URL, to task: WKURLSchemeTask, id: ObjectIdentifier) {
        guard let handle = try? FileHandle(forReadingFrom: file) else {
            _ = onMain(id) { task.didFailWithError(URLError(.cannotOpenFile)) }
            return
        }
        defer { try? handle.close() }
        let size = (try? file.resourceValues(forKeys: [.fileSizeKey]).fileSize) ?? 0
        let headers = [
            "Content-Type": Self.types[file.pathExtension.lowercased()] ?? "application/octet-stream",
            "Content-Length": String(size),
            "Cache-Control": "no-cache",
        ]
        let response = HTTPURLResponse(url: url, statusCode: 200, httpVersion: "HTTP/1.1", headerFields: headers)!
        guard onMain(id, { task.didReceive(response) }) else { return }
        while true {
            let chunk: Data
            do {
                chunk = try handle.read(upToCount: Self.chunkSize) ?? Data()
            } catch {
                _ = onMain(id) { task.didFailWithError(error) }
                return
            }
            if chunk.isEmpty { break }
            guard onMain(id, { task.didReceive(chunk) }) else { return }
        }
        _ = onMain(id) { task.didFinish() }
        DispatchQueue.main.async { self.live.remove(id) }
    }

    /// Runs body on the main thread unless WebKit already stopped the task
    /// (calling a stopped task throws). False once it's stopped.
    private func onMain(_ id: ObjectIdentifier, _ body: () -> Void) -> Bool {
        DispatchQueue.main.sync {
            guard live.contains(id) else { return false }
            body()
            return true
        }
    }
}
