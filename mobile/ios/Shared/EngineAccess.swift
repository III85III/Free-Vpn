import Foundation

enum EngineAccess {
    static let appGroup = "group.io.github.iii85iii.vpn"
    static let tunnelIdentifier = "io.github.iii85iii.vpn.tunnel"
    static let worker = DispatchQueue(label: "io.github.iii85iii.engine", attributes: .concurrent)
    static func directory() throws -> String {
        guard let group = FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: appGroup) else {
            throw failure("The signed app needs its shared App Group entitlement.")
        }
        let folder = group.appendingPathComponent("engine", isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true,
            attributes: [.posixPermissions: 0o700, .protectionKey: FileProtectionType.completeUntilFirstUserAuthentication])
        return folder.path
    }
    static func failure(_ message: String) -> NSError { NSError(domain: "III85III VPN", code: 1, userInfo: [NSLocalizedDescriptionKey: message]) }
    static func start(on: Bool) throws {
        var error: NSError?
        guard IIIEngine.start(try directory(), on: on, error: &error) else { throw error ?? failure("The local engine could not start.") }
    }
    static func request(_ method: String, _ path: String, _ body: [String: Any]? = nil) throws -> [String: Any] {
        let text = try body.map { String(decoding: try JSONSerialization.data(withJSONObject: $0), as: UTF8.self) } ?? ""
        var error: NSError?
        let response = IIIEngine.request(method, path: path, body: text, error: &error)
        if let error = error { throw error }
        guard let value = response?.data(using: .utf8), let result = try JSONSerialization.jsonObject(with: value) as? [String: Any] else { throw failure("The local response could not be read.") }
        return result
    }
    static func background<T>(_ action: @escaping () throws -> T) async throws -> T {
        try await withCheckedThrowingContinuation { continuation in worker.async { continuation.resume(with: Result { try action() }) } }
    }
}
