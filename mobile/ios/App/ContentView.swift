import SwiftUI

private let gold = Color(red: 217/255, green: 189/255, blue: 131/255)
private let surface = Color(red: 27/255, green: 32/255, blue: 40/255)

struct ContentView: View {
    @EnvironmentObject var model: VPNModel
    @State private var page = 0
    @State private var search = ""
    @State private var signIn = false
    @AppStorage("hide_email") private var hideEmail = true
    private let pages = ["Home", "Locations", "App rules", "Accounts", "Settings", "Help"]
    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            Text("III85III  /  VPN").font(.custom("Lato-Regular", size: 21)).foregroundColor(gold).padding(.top, 10)
            ScrollView(.horizontal, showsIndicators: false) { HStack(spacing: 6) { ForEach(pages.indices, id: \.self) { index in
                Button(pages[index]) { page = index; search = "" }.padding(.horizontal, 15).padding(.vertical, 11)
                    .background(page == index ? gold : surface).foregroundColor(page == index ? .black : .secondary).cornerRadius(13)
            } } }
            ScrollView { VStack(alignment: .leading, spacing: 18) { section }.frame(maxWidth: .infinity, alignment: .leading).padding(.vertical, 12) }
            Text(model.message).font(.footnote).foregroundColor(.secondary).frame(maxWidth: .infinity, alignment: .leading)
        }
        .font(.custom("Lato-Regular", size: 16)).padding(.horizontal, 20).padding(.bottom, 10)
        .background(Color(red: 16/255, green: 19/255, blue: 25/255).ignoresSafeArea())
        .task { await model.bootstrap() }
        .sheet(isPresented: $signIn) { SignInView().environmentObject(model) }
    }
    @ViewBuilder private var section: some View {
        switch page {
        case 0:
            title("Make yourself at home.", "Your account. Your connection.")
            VStack(spacing: 22) {
                Image(systemName: "power").font(.system(size: 62, weight: .light)).foregroundColor(gold).padding(28).background(surface).clipShape(Circle())
                Text(model.verified ? "You're connected." : model.connected ? "Getting you connected…" : "Ready when you are.").font(.title2).foregroundColor(gold)
                action(model.connected ? "Disconnect" : "Connect", prominent: true) { Task { if model.accounts.isEmpty && !model.connected { page = 3 } else { await model.toggle() } } }
                action("Choose a location") { page = 1 }
            }.frame(maxWidth: .infinity).padding(.vertical, 20)
            note("No personal VPS required. Mozilla eligibility and service quota apply.")
        case 1:
            title("Somewhere that suits you.", "Find a location and compare measured response times.")
            TextField("Search country or code", text: $search).textFieldStyle(.roundedBorder)
            action(model.working ? "Measuring…" : "Measure latency") { Task { await model.measure() } }.disabled(model.working)
            action("Recommended location") { Task { await model.select(""); page = 0 } }
            ForEach(visibleLocations.indices, id: \.self) { index in
                let location = visibleLocations[index]
                let code = location["code"] as? String ?? ""
                let name = location["name"] as? String ?? code
                let ping = location["ping_ms"] as? Int ?? 0
                action(name + (ping > 0 ? "  ·  \(ping) ms" : "")) { Task { await model.select(code); page = 0 } }
            }
        case 2:
            title("Your apps, your choice.", "Platform capabilities are different on iOS.")
            note("A normal personal iOS VPN cannot offer Android-style selection from every installed app. This build does not show a fake per-app selector or claim that only chosen apps are tunneled.")
            note("The packet tunnel routes supported device traffic. Enforced Telegram-bot-only routing is not implemented.")
        case 3:
            title("Your own accounts.", "Use your Mozilla password, not your Google password.")
            action("Add an account", prominent: true) { signIn = true }.disabled(!model.ready)
            Toggle("Hide email addresses", isOn: $hideEmail).tint(gold)
            if model.accounts.isEmpty { note("No accounts yet. Your sessions stay in this application's private shared container.") }
            ForEach(model.accounts.indices, id: \.self) { index in
                let account = model.accounts[index]
                let id = account["id"] as? String ?? ""
                let usage = account["usage"] as? [String: Any]
                VStack(alignment: .leading, spacing: 10) {
                    Text(hideEmail ? "••••••••" : account["email"] as? String ?? "Account").font(.headline)
                    Text(account["status"] as? String ?? "Unknown").foregroundColor(.secondary)
                    if let quota = usage?["remaining_bytes"] as? Double { Text(ByteCountFormatter.string(fromByteCount: Int64(quota), countStyle: .decimal) + " remaining") }
                    if let reset = usage?["reset_at"] as? String { Text("Renews: " + reset).font(.caption).foregroundColor(.secondary) }
                    action("Refresh balance") { Task { do { _ = try await model.request("POST", "/api/accounts/\(id)/refresh", [:]); await model.loadAccounts() } catch { model.message = error.localizedDescription } } }
                }.padding(16).background(surface).cornerRadius(15)
            }
        case 4:
            title("Make it yours.", "Native iOS interface. The same III85III identity.")
            note("iOS asks permission before adding this application's VPN configuration. Manage or remove it in Settings → VPN.")
            note("The tunnel extension uses Apple's public packetFlow interface. It does not depend on a private utun descriptor.")
            note("This is not a verified kill switch. Traffic can become direct after disconnect. Real-device testing is required before an App Store release.")
            note("Installing the unsigned build requires signing the application and its extension with your own Apple development credentials and App Group/Network Extension entitlements.")
        default:
            title("A little connection help.", "Check your account, network and selected location.")
            action("Check connection") { Task { await model.test() } }
            action("Measure server latency") { page = 1 }
            note("Mozilla access depends on the account, service and network. A successful browser login does not guarantee every service challenge succeeds here.")
            note("TCP and local DNS use the engine's proxy route. Arbitrary UDP and Telegram-bot-only routing are not promised.")
        }
    }
    private var visibleLocations: [[String: Any]] {
        model.locations.filter { search.isEmpty || ((($0["name"] as? String ?? "") + " " + ($0["code"] as? String ?? "")).localizedCaseInsensitiveContains(search)) }.sorted { left, right in (left["ping_ms"] as? Int ?? 0).positivePing < (right["ping_ms"] as? Int ?? 0).positivePing }
    }
    private func title(_ title: String, _ subtitle: String) -> some View { VStack(alignment: .leading, spacing: 8) { Text(title).font(.largeTitle.weight(.medium)); Text(subtitle).foregroundColor(.secondary).font(.subheadline) } }
    private func note(_ text: String) -> some View { Text(text).foregroundColor(.secondary).font(.subheadline).fixedSize(horizontal: false, vertical: true) }
    private func action(_ title: String, prominent: Bool = false, perform: @escaping () -> Void) -> some View { Button(action: perform) { Text(title).frame(maxWidth: .infinity).padding(.vertical, 16) }.foregroundColor(prominent ? .black : .primary).background(prominent ? gold : surface).cornerRadius(15) }
}
private extension Int { var positivePing: Int { self > 0 ? self : Int.max } }
