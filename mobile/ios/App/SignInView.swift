import SwiftUI
import UIKit

struct SignInView: View {
    @EnvironmentObject var model: VPNModel
    @Environment(\.dismiss) private var dismiss
    @State private var email = ""
    @State private var password = ""
    @State private var code = ""
    @State private var session = ""
    @State private var step = "credentials"
    @State private var message = "Sign in with your own Mozilla account."
    @State private var pending = false
    @State private var closed = false
    @State private var finished = false
    @State private var captcha: Data?
    @State private var polling: Task<Void, Never>?
    private let gold = Color(red: 217/255, green: 189/255, blue: 131/255)
    var body: some View {
        NavigationView {
            Form {
                Section(header: Text(step == "credentials" ? "Welcome to III85III" : step == "need_totp" ? "Enter your security code" : "Confirm your sign-in")) {
                    if step == "credentials" {
                        TextField("Mozilla email", text: $email).keyboardType(.emailAddress).textContentType(.username).autocapitalization(.none).disableAutocorrection(true)
                        SecureField("Mozilla password", text: $password).textContentType(.password)
                        Button(pending ? "Signing in…" : "Sign in") { Task { await begin() } }.disabled(pending || email.isEmpty || password.isEmpty)
                    } else if needsCode {
                        if let data = captcha, let image = UIImage(data: data) { Image(uiImage: image).resizable().scaledToFit().frame(maxHeight: 150) }
                        TextField("Verification code", text: $code).textContentType(.oneTimeCode).autocapitalization(.none).disableAutocorrection(true)
                        Button(pending ? "Verifying…" : "Verify") { Task { await verify() } }.disabled(pending || code.isEmpty)
                    } else { ProgressView("Waiting for Mozilla…") }
                    Text(message).font(.footnote).foregroundColor(.secondary)
                }
            }.navigationTitle("Mozilla account").navigationBarTitleDisplayMode(.inline).tint(gold)
                .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Cancel") { close(); dismiss() } } }
        }.onDisappear { close() }
    }
    private var needsCode: Bool { ["need_email_code", "need_totp", "need_unblock_code", "need_captcha"].contains(step) }
    private func begin() async {
        guard !pending, !closed else { return }; pending = true; defer { pending = false }
        let body = ["email": email.trimmingCharacters(in: .whitespacesAndNewlines), "password": password]; password = ""
        do { let state = try await model.request("POST", "/api/signin", body); let id = state["id"] as? String ?? ""; session = id
            if closed { await cancelSession(id); return }; await apply(state)
        } catch { if !closed { message = error.localizedDescription } }
    }
    private func verify() async {
        guard !pending, !closed else { return }; pending = true; defer { pending = false }; let value = code; code = ""
        do { let state = try await model.request("POST", "/api/signin/" + session, ["value": value]); if !closed { await apply(state) } } catch { if !closed { message = error.localizedDescription } }
    }
    private func apply(_ state: [String: Any]) async {
        guard !closed else { return }; step = state["step"] as? String ?? "waiting"; message = state["message"] as? String ?? "Waiting for Mozilla."
        if step == "done" { finished = true; closed = true; polling?.cancel(); await model.loadAccounts(); dismiss(); return }
        if ["failed", "error", "cancelled"].contains(step) { message = state["error"] as? String ?? message; await cancelSession(session); session = ""; step = "credentials"; return }
        if let encoded = state["captcha"] as? String, encoded.count < 1_500_000, encoded.hasPrefix("data:image/"), let comma = encoded.firstIndex(of: ",") { captcha = Data(base64Encoded: String(encoded[encoded.index(after: comma)...])) }
        polling?.cancel()
        if !needsCode { polling = Task { try? await Task.sleep(nanoseconds: 1_500_000_000); guard !Task.isCancelled, !closed else { return }; do { let value = try await model.request("GET", "/api/signin/" + session); await apply(value) } catch { if !closed { message = "Status could not refresh. Cancel and try again." } } } }
    }
    private func cancelSession(_ id: String) async { guard !id.isEmpty else { return }; _ = try? await model.request("DELETE", "/api/signin/" + id) }
    private func close() { guard !closed else { return }; closed = true; password = ""; code = ""; polling?.cancel(); let id = session; if !finished { Task { await cancelSession(id) } } }
}
