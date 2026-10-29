import SwiftUI

@main
struct III85IIIVPNApp: App {
    @StateObject private var model = VPNModel()
    var body: some Scene { WindowGroup { ContentView().environmentObject(model).preferredColorScheme(.dark) } }
}
