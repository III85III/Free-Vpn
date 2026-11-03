import Cocoa
import Darwin
import UniformTypeIdentifiers

enum AppFailure: Error { case unavailable, invalidAccountFile, engineStopped }

final class App: NSObject, NSApplicationDelegate, NSWindowDelegate {
    private var window: NSWindow!
    private let statusLabel = NSTextField(labelWithString: "Starting…")
    private let allowanceLabel = NSTextField(labelWithString: "")
    private let powerButton = NSButton(title: "Connect", target: nil, action: nil)
    private var polling = false
    private var busy = false
    private var isOn = false
    private var pollTimer: Timer?
    private var engine: Process?
    private var parentPipe: Pipe?
    private var base: URL!
    private var localToken = ""
    private var stopping = false
    private var checkStage = "window"
    private let smoke = CommandLine.arguments.contains("--smoke-test")
    private let session: URLSession = {
        let config = URLSessionConfiguration.ephemeral
        config.connectionProxyDictionary = [:]
        config.timeoutIntervalForRequest = 15
        return URLSession(configuration: config)
    }()
    private lazy var data: URL = {
        if smoke {
            return FileManager.default.temporaryDirectory.appendingPathComponent("iii85iii-mac-check-" + UUID().uuidString)
        }
        return FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("III85III VPN/user-data")
    }()

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.regular)
        makeMenu()
        window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 560, height: 540),
                          styleMask: [.titled, .closable, .miniaturizable, .resizable], backing: .buffered, defer: false)
        window.title = "III85III VPN"
        window.minSize = NSSize(width: 460, height: 480)
        window.appearance = NSAppearance(named: .darkAqua)
        window.backgroundColor = NSColor(calibratedRed: 0.106, green: 0.133, blue: 0.169, alpha: 1)
        let stack = NSStackView()
        stack.orientation = .vertical
        stack.alignment = .centerX
        stack.spacing = 24
        stack.translatesAutoresizingMaskIntoConstraints = false
        let brand = NSTextField(labelWithString: "III85III VPN")
        brand.font = .systemFont(ofSize: 32, weight: .semibold)
        brand.textColor = NSColor(calibratedRed: 0.9, green: 0.76, blue: 0.55, alpha: 1)
        statusLabel.font = .systemFont(ofSize: 18, weight: .medium)
        allowanceLabel.font = .systemFont(ofSize: 14)
        allowanceLabel.textColor = .secondaryLabelColor
        powerButton.target = self
        powerButton.action = #selector(togglePower)
        powerButton.bezelStyle = .rounded
        powerButton.controlSize = .large
        powerButton.font = .systemFont(ofSize: 19, weight: .semibold)
        powerButton.contentTintColor = brand.textColor
        powerButton.isEnabled = false
        powerButton.widthAnchor.constraint(equalToConstant: 240).isActive = true
        powerButton.heightAnchor.constraint(equalToConstant: 64).isActive = true
        let actions = NSStackView()
        actions.spacing = 12
        for (title, selector) in [("Servers", #selector(openServers)), ("Accounts", #selector(openAccounts)), ("Settings", #selector(openSettings))] {
            let button = NSButton(title: title, target: self, action: selector)
            button.bezelStyle = .rounded
            button.controlSize = .large
            actions.addArrangedSubview(button)
        }
        for view in [brand, statusLabel, powerButton, allowanceLabel, actions] as [NSView] { stack.addArrangedSubview(view) }
        window.contentView!.addSubview(stack)
        NSLayoutConstraint.activate([
            stack.centerXAnchor.constraint(equalTo: window.contentView!.centerXAnchor),
            stack.centerYAnchor.constraint(equalTo: window.contentView!.centerYAnchor),
            stack.leadingAnchor.constraint(greaterThanOrEqualTo: window.contentView!.leadingAnchor, constant: 24),
            stack.trailingAnchor.constraint(lessThanOrEqualTo: window.contentView!.trailingAnchor, constant: -24)
        ])
        window.delegate = self
        window.center()
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        Task { await start() }
    }

    private func makeMenu() {
        let menu = NSMenu()
        let appItem = NSMenuItem()
        let appMenu = NSMenu()
        appMenu.addItem(withTitle: "About III85III VPN", action: #selector(about), keyEquivalent: "")
        appMenu.addItem(.separator())
        appMenu.addItem(withTitle: "Quit III85III VPN", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        appItem.submenu = appMenu
        menu.addItem(appItem)
        let accountItem = NSMenuItem()
        let accounts = NSMenu(title: "Account")
        accounts.addItem(withTitle: "Import private account file…", action: #selector(importAccount), keyEquivalent: "i")
        accountItem.submenu = accounts
        menu.addItem(accountItem)
        let editItem = NSMenuItem()
        let edit = NSMenu(title: "Edit")
        edit.addItem(withTitle: "Cut", action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        edit.addItem(withTitle: "Copy", action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        edit.addItem(withTitle: "Paste", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        edit.addItem(withTitle: "Select All", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")
        editItem.submenu = edit
        menu.addItem(editItem)
        let connectionItem = NSMenuItem()
        let connection = NSMenu(title: "Connection")
        connection.addItem(withTitle: "Install Whole Mac helper…", action: #selector(installHelper), keyEquivalent: "")
        connectionItem.submenu = connection
        menu.addItem(connectionItem)
        let helpItem = NSMenuItem()
        let help = NSMenu(title: "Help")
        help.addItem(withTitle: "Telegram support", action: #selector(support), keyEquivalent: "")
        helpItem.submenu = help
        menu.addItem(helpItem)
        NSApp.mainMenu = menu
    }

    private func freePort() throws -> UInt16 {
        let fd = socket(AF_INET, SOCK_STREAM, 0)
        guard fd >= 0 else { throw AppFailure.unavailable }
        defer { close(fd) }
        var address = sockaddr_in()
        address.sin_len = UInt8(MemoryLayout<sockaddr_in>.size)
        address.sin_family = sa_family_t(AF_INET)
        address.sin_addr.s_addr = inet_addr("127.0.0.1")
        let result = withUnsafePointer(to: &address) {
            $0.withMemoryRebound(to: sockaddr.self, capacity: 1) { Darwin.bind(fd, $0, socklen_t(MemoryLayout<sockaddr_in>.size)) }
        }
        guard result == 0 else { throw AppFailure.unavailable }
        var length = socklen_t(MemoryLayout<sockaddr_in>.size)
        let named = withUnsafeMutablePointer(to: &address) {
            $0.withMemoryRebound(to: sockaddr.self, capacity: 1) { getsockname(fd, $0, &length) }
        }
        guard named == 0 else { throw AppFailure.unavailable }
        return UInt16(bigEndian: address.sin_port)
    }

    @MainActor private func start() async {
        do {
            try FileManager.default.createDirectory(at: data, withIntermediateDirectories: true,
                                                    attributes: [.posixPermissions: 0o700])
            // A second window must not rewrite a running engine's configuration.
            let lock = data.appendingPathComponent("mac-app.lock")
            let lockFD = open(lock.path, O_CREAT | O_RDWR, 0o600)
            guard lockFD >= 0, flock(lockFD, LOCK_EX | LOCK_NB) == 0 else { throw AppFailure.unavailable }
            // Retained until this app process exits.
            let port = try freePort()
            base = URL(string: "http://127.0.0.1:\(port)")!
            let configFile = data.appendingPathComponent("config.json")
            var config: [String: Any] = [:]
            if FileManager.default.fileExists(atPath: configFile.path) {
                guard let saved = try JSONSerialization.jsonObject(with: Data(contentsOf: configFile)) as? [String: Any] else {
                    throw AppFailure.unavailable
                }
                config = saved
            }
            var dashboard = config["dashboard"] as? [String: Any] ?? [:]
            dashboard["enabled"] = true
            dashboard["listen"] = "127.0.0.1:\(port)"
            // The discarded random password cannot be used to log in. This
            // window authenticates with the engine's one-use local login link.
            let authFile = Bundle.main.resourceURL!.appendingPathComponent("dashboard-auth.json")
            let auth = try JSONSerialization.jsonObject(with: Data(contentsOf: authFile)) as! [String: String]
            dashboard["username"] = "local-app"
            dashboard["password_hash"] = auth["password_hash"]!
            config["dashboard"] = dashboard
            var proxy = config["proxy"] as? [String: Any] ?? [:]
            var proxyPort = try freePort()
            while proxyPort == port { proxyPort = try freePort() }
            proxy["listen"] = "127.0.0.1:\(proxyPort)"
            config["proxy"] = proxy
            // First launch never silently changes macOS network settings.
            if config["system_proxy"] == nil { config["system_proxy"] = ["mode": "set"] }
            try JSONSerialization.data(withJSONObject: config).write(to: configFile, options: .atomic)
            try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: configFile.path)
            let child = Process()
            child.executableURL = Bundle.main.resourceURL!.appendingPathComponent("core/iii85iii-core")
            child.arguments = ["-app", "-data-dir", data.path]
            let input = Pipe()
            child.standardInput = input
            child.standardOutput = FileHandle.nullDevice
            child.standardError = FileHandle.nullDevice
            child.terminationHandler = { [weak self] _ in
                DispatchQueue.main.async { [weak self] in
                    guard let self = self, !self.stopping else { return }
                    self.fail("The local engine stopped. Close the app and try again.")
                }
            }
            engine = child
            parentPipe = input
            checkStage = "engine-start"
            try child.run()
            var ready = false
            for _ in 0..<100 {
                guard child.isRunning else { throw AppFailure.engineStopped }
                if let token = try? String(contentsOf: data.appendingPathComponent("local-api.token"), encoding: .utf8) {
                    localToken = token.trimmingCharacters(in: .whitespacesAndNewlines)
                    if !localToken.isEmpty, let _ = try? await request("GET", "/api/status") { ready = true; break }
                }
                try await Task.sleep(nanoseconds: 100_000_000)
            }
            guard ready else { throw AppFailure.unavailable }
            checkStage = "local-api"
            if smoke {
                let status = try await request("GET", "/api/status")
                let accounts = try await request("GET", "/api/accounts")
                guard (accounts["accounts"] as? [Any])?.isEmpty == true,
                      status["system_proxy_supported"] as? Bool == true else { throw AppFailure.unavailable }
                var unauth = URLRequest(url: base.appendingPathComponent("api/status"))
                unauth.timeoutInterval = 3
                let (_, response) = try await session.data(for: unauth)
                guard (response as? HTTPURLResponse)?.statusCode == 401 else { throw AppFailure.unavailable }
            }
            render(try await request("GET", "/api/status"))
            if smoke {
                checkStage = "browser-session"
                let url = try await dashboardURL("accounts")
                let (html, response) = try await session.data(for: URLRequest(url: url))
                guard (response as? HTTPURLResponse)?.statusCode == 200,
                      String(data: html, encoding: .utf8)?.contains("app.js") == true else { throw AppFailure.unavailable }
                let (_, authenticated) = try await session.data(for: URLRequest(url: base.appendingPathComponent("api/status")))
                guard (authenticated as? HTTPURLResponse)?.statusCode == 200,
                      powerButton.isEnabled, statusLabel.stringValue == "Disconnected" else { throw AppFailure.unavailable }
                print("PASS: macOS app opens, native controls ready, local API authenticated, fresh account list empty, browser dashboard and session work. No Mozilla account or external VPN connection tested.")
                stop()
                NSApp.terminate(nil)
            } else {
                pollTimer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in self?.poll() }
            }
        } catch { fail("III85III VPN could not start. Quit any other copy, then try again. Your saved accounts have not been removed.") }
    }

    private func request(_ method: String, _ path: String, _ body: [String: Any]? = nil) async throws -> [String: Any] {
        guard let base = base, !localToken.isEmpty else { throw AppFailure.unavailable }
        var request = URLRequest(url: URL(string: path, relativeTo: base)!)
        request.httpMethod = method
        request.setValue(localToken, forHTTPHeaderField: "X-Local-Token")
        if method != "GET" {
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
            request.httpBody = try JSONSerialization.data(withJSONObject: body ?? [:])
        }
        let (bytes, response) = try await session.data(for: request)
        guard let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode),
              let json = try JSONSerialization.jsonObject(with: bytes) as? [String: Any] else { throw AppFailure.unavailable }
        return json
    }

    private func dashboardURL(_ page: String) async throws -> URL {
        let link = try await request("POST", "/api/session/link", [:])
        guard let path = link["path"] as? String, path.hasPrefix("/app/login/"),
              let url = URL(string: path + "#" + page, relativeTo: base)?.absoluteURL else { throw AppFailure.unavailable }
        return url
    }

    @MainActor private func openDashboard(_ page: String) {
        Task { @MainActor in
            do {
                guard NSWorkspace.shared.open(try await dashboardURL(page)) else { throw AppFailure.unavailable }
            } catch { showMessage("Could not open the local page", "Wait for the engine to finish starting, then try again.") }
        }
    }
    @objc private func openServers() { openDashboard("locations") }
    @objc private func openAccounts() { openDashboard("accounts") }
    @objc private func openSettings() { openDashboard("settings") }

    private func render(_ value: [String: Any]) {
        let state = value["state"] as? [String: Any] ?? [:]
        let phase = state["phase"] as? String ?? "off"
        isOn = phase != "off" && phase != "error"
        statusLabel.stringValue = phase == "off" ? "Disconnected" : phase == "error" ? "Connection problem" : "Connecting…"
        if phase == "running", value["proxy"] != nil {
            let test = value["last_test"] as? [String: Any] ?? [:]
            let formatter = ISO8601DateFormatter()
            formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
            func date(_ raw: Any?) -> Date? {
                guard let raw = raw as? String else { return nil }
                if let result = formatter.date(from: raw) { return result }
                let fallback = ISO8601DateFormatter()
                return fallback.date(from: raw)
            }
            if test["ok"] as? Bool == true, let at = date(test["at"]), let since = date(state["since"]),
               at >= since, at >= (date(value["network_changed_at"]) ?? .distantPast) { statusLabel.stringValue = "Connected" }
        }
        if busy { powerButton.title = "Please wait…" }
        else { powerButton.title = isOn ? (statusLabel.stringValue == "Connected" ? "Disconnect" : "Cancel connection") : "Connect" }
        powerButton.isEnabled = !busy
        if let account = value["active_account"] as? [String: Any], let usage = account["usage"] as? [String: Any] {
            if usage["unlimited"] as? Bool == true { allowanceLabel.stringValue = "Unlimited allowance" }
            else if let remaining = usage["remaining_bytes"] as? NSNumber {
                allowanceLabel.stringValue = ByteCountFormatter.string(fromByteCount: max(0, remaining.int64Value), countStyle: .decimal) + " remaining"
            } else { allowanceLabel.stringValue = "Allowance unavailable" }
        } else { allowanceLabel.stringValue = "Your accounts stay on this Mac" }
    }

    private func poll() {
        guard !polling, !stopping else { return }
        polling = true
        Task { @MainActor in
            defer { polling = false }
            do { render(try await request("GET", "/api/status")) }
            catch { statusLabel.stringValue = "Connection status unavailable" }
        }
    }

    @objc private func togglePower() {
        guard !busy, base != nil, !localToken.isEmpty else { return }
        let turnOn = !isOn
        busy = true
        powerButton.isEnabled = false
        powerButton.title = turnOn ? "Connecting…" : "Disconnecting…"
        Task { @MainActor in
            defer { busy = false; powerButton.isEnabled = true; poll() }
            do {
                if turnOn {
                    let reply = try await request("GET", "/api/accounts")
                    guard let accounts = reply["accounts"] as? [Any], !accounts.isEmpty else {
                        let alert = NSAlert()
                        alert.messageText = "Add an account to connect"
                        alert.informativeText = "Sign in with Mozilla, or import an existing private account file. This download does not include shared accounts."
                        alert.addButton(withTitle: "Open Accounts")
                        alert.addButton(withTitle: "Import file")
                        alert.addButton(withTitle: "Cancel")
                        let choice = alert.runModal()
                        if choice == .alertFirstButtonReturn { openAccounts() }
                        if choice == .alertSecondButtonReturn { importAccount() }
                        return
                    }
                }
                _ = try await request("POST", "/api/power", ["on": turnOn])
            } catch { showMessage("Could not change the connection", "Check Accounts and Settings for details, then try again.") }
        }
    }

    private func showMessage(_ title: String, _ message: String) {
        let alert = NSAlert()
        alert.messageText = title
        alert.informativeText = message
        alert.runModal()
    }

    @objc private func importAccount() {
        let panel = NSOpenPanel()
        panel.title = "Import a private account file"
        panel.allowedContentTypes = [.json]
        panel.allowsMultipleSelection = false
        guard panel.runModal() == .OK, let file = panel.url else { return }
        Task { @MainActor in
            do {
                let bytes = try Data(contentsOf: file)
                guard bytes.count <= 1_048_576,
                      let json = try JSONSerialization.jsonObject(with: bytes) as? [String: Any] else { throw AppFailure.invalidAccountFile }
                let records = (json["accounts"] as? [[String: Any]]) ?? [json]
                guard !records.isEmpty, records.count <= 200 else { throw AppFailure.invalidAccountFile }
                for account in records {
                    guard let email = account["email"] as? String, email.contains("@"),
                          let token = account["refresh_token"] as? String, !token.isEmpty else { throw AppFailure.invalidAccountFile }
                }
                for account in records {
                    _ = try await request("POST", "/api/accounts/import", ["email": account["email"]!, "refresh_token": account["refresh_token"]!])
                }
                let message = NSAlert()
                message.messageText = "Accounts imported"
                message.informativeText = "The accounts are stored on this Mac. Availability and remaining allowance still need to be checked by Mozilla."
                message.runModal()
                poll()
            } catch {
                let message = NSAlert()
                message.messageText = "Account import could not finish"
                message.informativeText = "Use a private JSON file with email and refresh_token fields. Already imported accounts are kept."
                message.runModal()
            }
        }
    }

    @objc private func installHelper() {
        let confirmation = NSAlert()
        confirmation.messageText = "Enable Whole Mac mode?"
        confirmation.informativeText = "macOS will ask for administrator permission to install the network helper. This first macOS build has not been validated with a real VPN account."
        confirmation.addButton(withTitle: "Install helper")
        confirmation.addButton(withTitle: "Cancel")
        guard confirmation.runModal() == .alertFirstButtonReturn else { return }
        DispatchQueue.global(qos: .userInitiated).async {
            let helper = Process()
            helper.executableURL = Bundle.main.resourceURL!.appendingPathComponent("core/firefox-vpn-tun")
            helper.arguments = ["install", "-prompt", "III85III VPN needs permission to route this Mac's connections."]
            helper.standardOutput = FileHandle.nullDevice
            helper.standardError = FileHandle.nullDevice
            let ok: Bool
            do { try helper.run(); helper.waitUntilExit(); ok = helper.terminationStatus == 0 } catch { ok = false }
            DispatchQueue.main.async {
                let message = NSAlert()
                message.messageText = ok ? "Helper installed" : "Helper was not installed"
                message.informativeText = ok ? "Choose TUN in Settings to use Whole Mac mode. UDP and app-rule limits still apply." : "Administrator permission may have been cancelled. System proxy mode remains available."
                message.runModal()
            }
        }
    }

    @objc private func about() {
        NSApp.orderFrontStandardAboutPanel(options: [.applicationName: "III85III VPN", .applicationVersion: "1.0",
            .credits: NSAttributedString(string: "Free desktop client. Service access and allowance depend on Mozilla. macOS build is not notarized. Dependency licenses are included in the app.")])
    }
    @objc private func support() { NSWorkspace.shared.open(URL(string: "https://t.me/cabicy")!) }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
    func applicationWillTerminate(_ notification: Notification) { stop() }
    private func stop() {
        guard !stopping else { return }
        stopping = true
        pollTimer?.invalidate()
        try? parentPipe?.fileHandleForWriting.close()
        // The engine restores network settings when its stdin closes.
        if let engine = engine, engine.isRunning {
            let deadline = Date().addingTimeInterval(5)
            while engine.isRunning, Date() < deadline { Thread.sleep(forTimeInterval: 0.05) }
            if engine.isRunning { engine.terminate() }
        }
        if smoke { try? FileManager.default.removeItem(at: data) }
    }
    private func fail(_ message: String) {
        if smoke { fputs("FAIL: macOS application check failed at \(checkStage).\n", stderr); stop(); exit(1) }
        let alert = NSAlert()
        alert.messageText = "Connection problem"
        alert.informativeText = message
        alert.runModal()
        NSApp.terminate(nil)
    }
}

let application = NSApplication.shared
let delegate = App()
application.delegate = delegate
application.run()
