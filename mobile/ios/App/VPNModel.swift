import Foundation
import Combine
import NetworkExtension

private final class ProviderReply {
    private let lock = NSLock()
    private var continuation: CheckedContinuation<[String: Any], Error>?
    init(_ continuation: CheckedContinuation<[String: Any], Error>) { self.continuation = continuation }
    func finish(_ result: Result<[String: Any], Error>) {
        lock.lock(); let target = continuation; continuation = nil; lock.unlock()
        target?.resume(with: result)
    }
}

@MainActor final class VPNModel: ObservableObject {
    @Published var ready = false
    @Published var connected = false
    @Published var working = false
    @Published var message = "Starting your local engine…"
    @Published var status: [String: Any] = [:]
    @Published var locations: [[String: Any]] = []
    @Published var accounts: [[String: Any]] = []
    private var manager: NETunnelProviderManager?
    private var refresh: Task<Void, Never>?
    private var observer: NSObjectProtocol?
    private var bootstrapping = false
    private var warming = false
    private var nextWarmup = Date.distantPast
    func bootstrap() async {
        guard !ready, !bootstrapping else { return }; bootstrapping = true
        defer { bootstrapping = false }
        do {
            let values: [NETunnelProviderManager] = try await withCheckedThrowingContinuation { continuation in NETunnelProviderManager.loadAllFromPreferences { values, error in
                if let error = error { continuation.resume(throwing: error) } else { continuation.resume(returning: values ?? []) }
            }}
            manager = values.first { ($0.protocolConfiguration as? NETunnelProviderProtocol)?.providerBundleIdentifier == EngineAccess.tunnelIdentifier }
            connected = manager?.connection.status == .connected || manager?.connection.status == .connecting
            if !connected { try await EngineAccess.background { try EngineAccess.start(on: false) } }
            ready = true; message = ""
            observer = NotificationCenter.default.addObserver(forName: .NEVPNStatusDidChange, object: nil, queue: .main) { [weak self] _ in Task { @MainActor in await self?.connectionChanged() } }
            refresh = Task { [weak self] in while !Task.isCancelled { await self?.poll(); try? await Task.sleep(nanoseconds: 2_000_000_000) } }
            await loadAccounts(); await loadLocations()
        } catch { message = error.localizedDescription }
    }
    func request(_ method: String, _ path: String, _ body: [String: Any]? = nil) async throws -> [String: Any] {
        if let session = manager?.connection as? NETunnelProviderSession, session.status == .connected {
            let data = try JSONSerialization.data(withJSONObject: ["method": method, "path": path, "body": body ?? [:]])
            return try await withCheckedThrowingContinuation { continuation in
                let reply = ProviderReply(continuation)
                let timeout: TimeInterval = path == "/api/locations/ping" || path.hasPrefix("/api/signin") ? 70 : path == "/api/test" ? 25 : 10
                let deadline = DispatchWorkItem { reply.finish(.failure(EngineAccess.failure("The tunnel request timed out. You can disconnect from iOS Settings."))) }
                DispatchQueue.global().asyncAfter(deadline: .now() + timeout, execute: deadline)
                do { try session.sendProviderMessage(data) { response in
                    deadline.cancel()
                    do { guard let response = response, let envelope = try JSONSerialization.jsonObject(with: response) as? [String: Any], let value = envelope["result"] as? [String: Any] else { throw EngineAccess.failure("The tunnel request could not finish.") }; reply.finish(.success(value)) } catch { reply.finish(.failure(error)) }
                }} catch { deadline.cancel(); reply.finish(.failure(error)) }
            }
        }
        guard !connected else { throw EngineAccess.failure("The connection is starting. Try again in a moment.") }
        return try await EngineAccess.background { try EngineAccess.request(method, path, body) }
    }
    func poll() async { guard ready else { return }; do { status = try await request("GET", "/api/status"); warmConnection() } catch { status = [:]; if !working { message = "Status could not refresh. Check the connection in iOS Settings." } } }
    func loadAccounts() async { do { accounts = try await request("GET", "/api/accounts")["accounts"] as? [[String: Any]] ?? [] } catch { message = error.localizedDescription } }
    func loadLocations() async { do { locations = try await request("GET", "/api/locations")["locations"] as? [[String: Any]] ?? [] } catch { message = error.localizedDescription } }
    func toggle() async {
        guard ready, !working else { return }
        if connected { manager?.connection.stopVPNTunnel(); return }
        guard !accounts.isEmpty else { message = "Add your Mozilla account first."; return }
        working = true; defer { working = false }
        do {
            let selected = manager ?? NETunnelProviderManager()
            let configuration = NETunnelProviderProtocol(); configuration.providerBundleIdentifier = EngineAccess.tunnelIdentifier; configuration.serverAddress = "III85III VPN"
            selected.protocolConfiguration = configuration; selected.localizedDescription = "III85III VPN"; selected.isEnabled = true
            try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in selected.saveToPreferences { error in if let error = error { continuation.resume(throwing: error) } else { continuation.resume() } } }
            try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in selected.loadFromPreferences { error in if let error = error { continuation.resume(throwing: error) } else { continuation.resume() } } }
            try await EngineAccess.background { IIIEngine.stop() }
            manager = selected; connected = true
            try selected.connection.startVPNTunnel(); message = "Starting the VPN connection…"
        } catch { connected = false; message = error.localizedDescription; try? await EngineAccess.background { try EngineAccess.start(on: false) } }
    }
    private func connectionChanged() async {
        let state = manager?.connection.status ?? .disconnected
        connected = state == .connected || state == .connecting || state == .reasserting
        if state == .disconnected || state == .invalid { try? await EngineAccess.background { try EngineAccess.start(on: false) }; message = "Disconnected." }
        await poll()
    }
    func select(_ code: String) async { do { _ = try await request("POST", "/api/location", ["country": code]); message = "Location selected." } catch { message = error.localizedDescription } }
    func measure() async { guard !working else { return }; working = true; defer { working = false }; do { _ = try await request("POST", "/api/locations/ping", ["codes": []]); await loadLocations(); message = "Location measurements updated." } catch { message = error.localizedDescription } }
    func test() async { do { let value = try await request("POST", "/api/test", [:]); message = (value["ok"] as? Bool == true) ? "Connection verified." : "Connection could not be verified." } catch { message = error.localizedDescription } }
    private func timestamp(_ value: Any?) -> Date? { guard let text = value as? String else { return nil }; let format = ISO8601DateFormatter(); format.formatOptions = [.withInternetDateTime, .withFractionalSeconds]; if let date = format.date(from: text) { return date }; format.formatOptions = [.withInternetDateTime]; return format.date(from: text) }
    var verified: Bool {
        guard manager?.connection.status == .connected, let state = status["state"] as? [String: Any], state["phase"] as? String == "running", let test = status["last_test"] as? [String: Any], test["ok"] as? Bool == true, let tested = timestamp(test["at"]), let started = timestamp(state["since"]), tested >= started else { return false }
        if let changed = timestamp(status["network_changed_at"]), tested < changed { return false }; return true
    }
    private func warmConnection() {
        guard manager?.connection.status == .connected, !verified, !warming, Date() >= nextWarmup else { return }
        warming = true; nextWarmup = Date().addingTimeInterval(30)
        Task { defer { warming = false }; _ = try? await request("POST", "/api/test", [:]) }
    }
}
