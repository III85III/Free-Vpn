import NetworkExtension
import Foundation
import Darwin

final class PacketTunnelProvider: NEPacketTunnelProvider {
    private let state = NSLock()
    private var stopped = true
    private var monitor: DispatchSourceTimer?
    private func isStopped() -> Bool { state.lock(); defer { state.unlock() }; return stopped }
    override func startTunnel(options: [String: NSObject]? = nil, completionHandler: @escaping (Error?) -> Void) {
        state.lock(); stopped = false; state.unlock()
        EngineAccess.worker.async {
            do {
                try EngineAccess.start(on: true)
                IIIEngine.connect()
                var error: NSError?
                guard IIIEngine.startPackets(&error) else { throw error ?? EngineAccess.failure("The packet tunnel could not start.") }
                let settings = NEPacketTunnelNetworkSettings(tunnelRemoteAddress: "127.0.0.1")
                settings.mtu = 1500
                let v4 = NEIPv4Settings(addresses: ["172.19.0.1"], subnetMasks: ["255.255.255.252"])
                v4.includedRoutes = [.default()]; settings.ipv4Settings = v4
                let v6 = NEIPv6Settings(addresses: ["fdfe:dcba:9876::1"], networkPrefixLengths: [126])
                v6.includedRoutes = [.default()]; settings.ipv6Settings = v6
                let dns = NEDNSSettings(servers: ["172.19.0.2"]); dns.matchDomains = [""]; settings.dnsSettings = dns
                self.setTunnelNetworkSettings(settings) { error in
                    if let error = error { self.cleanup(); completionHandler(error); return }
                    if self.isStopped() { self.cleanup(); completionHandler(EngineAccess.failure("Connection cancelled.")); return }
                    self.readPackets(); self.writePackets(); self.startMonitor(); completionHandler(nil)
                }
            } catch { self.cleanup(); completionHandler(error) }
        }
    }
    private func readPackets() {
        guard !isStopped() else { return }
        packetFlow.readPackets { packets, _ in
            guard !self.isStopped() else { return }
            for packet in packets {
                var error: NSError?
                if !IIIEngine.writePacket(packet, error: &error) { self.cancelTunnelWithError(error); return }
            }
            self.readPackets()
        }
    }
    private func writePackets() {
        EngineAccess.worker.async {
            while !self.isStopped(), let packet = IIIEngine.readPacket(), !packet.isEmpty {
                guard let version = packet.first else { continue }
                let family = version >> 4 == 6 ? AF_INET6 : AF_INET
                self.packetFlow.writePackets([packet], withProtocols: [NSNumber(value: family)])
            }
        }
    }
    private func startMonitor() {
        let timer = DispatchSource.makeTimerSource(queue: EngineAccess.worker)
        timer.schedule(deadline: .now() + 2, repeating: 2)
        timer.setEventHandler { [weak self] in
            guard let self = self, !self.isStopped() else { return }
            if !IIIEngine.connectionOn() { self.cancelTunnelWithError(EngineAccess.failure("The connection stopped. Check your account or network.")) }
        }
        monitor = timer; timer.resume()
    }
    private func cleanup() {
        state.lock(); stopped = true; state.unlock(); monitor?.cancel(); monitor = nil
        IIIEngine.stopPackets(); IIIEngine.stop()
    }
    override func stopTunnel(with reason: NEProviderStopReason, completionHandler: @escaping () -> Void) {
        EngineAccess.worker.async { self.cleanup(); completionHandler() }
    }
    override func handleAppMessage(_ messageData: Data, completionHandler: ((Data?) -> Void)? = nil) {
        guard messageData.count < 128 * 1024 else { completionHandler?(nil); return }
        EngineAccess.worker.async {
            do {
                guard let message = try JSONSerialization.jsonObject(with: messageData) as? [String: Any],
                      let method = message["method"] as? String, let path = message["path"] as? String else { throw EngineAccess.failure("Invalid application message.") }
                let value = try EngineAccess.request(method, path, message["body"] as? [String: Any])
                completionHandler?(try JSONSerialization.data(withJSONObject: ["result": value]))
            } catch { completionHandler?(try? JSONSerialization.data(withJSONObject: ["error": "The tunnel request could not finish."])) }
        }
    }
}
