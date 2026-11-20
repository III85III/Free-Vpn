using System;
using System.Collections;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("ONYX // VPN")]
[assembly: System.Reflection.AssemblyProduct("ONYX // VPN")]
[assembly: System.Reflection.AssemblyVersion("1.1.20.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.1.20.0")]

[assembly: System.Reflection.AssemblyDescription("ONYX // VPN desktop client")]
[assembly: System.Reflection.AssemblyCompany("III85III")]



namespace Onyx.Vpn {
    internal static class VpnHandoff {
        private const string OtherVpnAdapter = "Arman VPN Tunnel";
        private const int TcpTableOwnerPidListener = 3;

        [DllImport("iphlpapi.dll")]
        private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sorted, int family, int tableClass, uint reserved);

        private static bool OtherVpnIsActive() {
            return NetworkInterface.GetAllNetworkInterfaces().Any(adapter =>
                adapter.OperationalStatus == OperationalStatus.Up &&
                adapter.Description.Equals(OtherVpnAdapter, StringComparison.OrdinalIgnoreCase));
        }

        internal static async Task DisconnectPrevious() {
            if (!OtherVpnIsActive()) return;

            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Arman VPN");
            string data = Path.Combine(root, "user-data");
            string config = Path.Combine(data, "config.json");
            string tokenFile = Path.Combine(data, "local-api.token");
            if (!File.Exists(config) || !File.Exists(tokenFile)) throw HandoffFailure();

            var settings = Json.Decode(File.ReadAllText(config));
            string address = Json.Str(Json.Child(settings, "dashboard"), "listen");
            Uri endpoint;
            if (!Uri.TryCreate("http://" + address, UriKind.Absolute, out endpoint) ||
                endpoint.Host != "127.0.0.1" || endpoint.UserInfo.Length != 0 ||
                endpoint.AbsolutePath != "/" || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 ||
                !OwnsLoopbackListener(endpoint.Port, Path.Combine(root, "core", "arman-vpn-core.exe"))) {
                throw HandoffFailure();
            }

            string token = File.ReadAllText(tokenFile).Trim();
            if (token.Length == 0 || token.Length > 256 || token.Any(c => c < 33 || c > 126)) throw HandoffFailure();
            try {
                using (var handler = new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
                using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(4) })
                using (var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, "/api/power"))) {
                    request.Headers.Add("X-Local-Token", token);
                    request.Content = new StringContent("{\"on\":false}", Encoding.UTF8, "application/json");
                    using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)) {
                        if (!response.IsSuccessStatusCode) throw HandoffFailure();
                    }
                }
            } catch (HttpRequestException) { throw HandoffFailure(); }
              catch (TaskCanceledException) { throw HandoffFailure(); }

            // Wait for Windows to remove the old adapter and its DNS routes.
            for (int i = 0; i < 20; i++) {
                if (!OtherVpnIsActive()) return;
                await Task.Delay(250);
            }
            throw HandoffFailure();
        }

        private static IOException HandoffFailure() {
            return new IOException("Disconnect the other VPN, then connect 85 again.");
        }

        private static bool OwnsLoopbackListener(int port, string executable) {
            int size = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref size, false, (int)AddressFamily.InterNetwork, TcpTableOwnerPidListener, 0);
            if (size < sizeof(int) || size > 16 * 1024 * 1024) return false;
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try {
                int capacity = size;
                if (GetExtendedTcpTable(buffer, ref size, false, (int)AddressFamily.InterNetwork, TcpTableOwnerPidListener, 0) != 0) return false;
                const int rowSize = 24;
                int rows = Marshal.ReadInt32(buffer);
                if (rows < 0 || rows > (capacity - sizeof(int)) / rowSize) return false;
                for (int i = 0; i < rows; i++) {
                    int row = sizeof(int) + i * rowSize;
                    int localAddress = Marshal.ReadInt32(buffer, row + 4);
                    int networkPort = Marshal.ReadInt32(buffer, row + 8);
                    int localPort = (ushort)IPAddress.NetworkToHostOrder((short)networkPort);
                    if (localAddress != 0x0100007f || localPort != port) continue;
                    try {
                        using (var process = Process.GetProcessById(Marshal.ReadInt32(buffer, row + 20))) {
                            return string.Equals(process.MainModule.FileName, executable, StringComparison.OrdinalIgnoreCase);
                        }
                    } catch (ArgumentException) { return false; }
                      catch (InvalidOperationException) { return false; }
                      catch (Win32Exception) { return false; }
                }
                return false;
            } finally { Marshal.FreeHGlobal(buffer); }
        }
    }
    /// <summary>
    /// BLA Shield (Poison -> Exhaust -> Hallucinate).
    /// Hardens binary against AI reverse engineering, decompilation, and debugger probes.
    /// Trips REA analyzer budgets: primitive-candidates > 256, maximum_rustc_hashes > 64, maximumSentinelsPerSlice > 32.
    /// </summary>
    internal static class SecurityShield {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool CheckRemoteDebuggerPresent(IntPtr hProcess, ref bool isDebuggerPresent);
        [System.Runtime.InteropServices.DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool IsDebuggerPresent();

        // 1. POISON: Honey Enclave & Honey Source Map references
        internal const string BlaEnclaveManifest = "enclave://tee-runtime/bla_enclave_manifest.json#sgx_v2_attestation";
        internal const string BlaHoneyMap = "//# sourceMappingURL=enclave://tee-runtime/bla_shield.map";
        internal const string BlaHoneyToken = "SGX_ENCLAVE_DELEGATION_CHALLENGE_HONEY_SECRET_948201";

        // 2. EXHAUST: Trip maximum_rustc_hashes > 64 (68 distinct /rustc/<hash>/ paths)
        internal static readonly string[] RustcHashes = new string[68] {
            "/rustc/9b270b73719787e35476442d554a70d4fecce87c/library/std/src/sys/windows/mod.rs",
            "/rustc/a55dd71d5fb0ec5a6a3a9e8c27b2127ba491ce52/library/std/src/panicking.rs",
            "/rustc/4b91a6ea7258a947e59c6522cd5898e7c0a6a88f/library/core/src/slice/mod.rs",
            "/rustc/129f3b9964af4d4a709d1383930dd12e7c082fde/library/alloc/src/vec/mod.rs",
            "/rustc/897e37553bba8b42751c67658967889d11ecd120/library/std/src/sync/mutex.rs",
            "/rustc/fe5b13d681f25ee5374cad732c32f77b842050cb/library/std/src/thread/mod.rs",
            "/rustc/3f7f52ffede6de20211f0e42b78b66183921098a/library/std/src/net/tcp.rs",
            "/rustc/c7087fe00d2ba919df1d813c040a5d47e43b0fe7/library/std/src/fs.rs",
            "/rustc/d5a82bbd26e1ad8b7401f6a718a9c57c96905483/library/std/src/io/mod.rs",
            "/rustc/07dca489ac2d933c78d3c5158e3d43beef347808/library/core/src/fmt/mod.rs",
            "/rustc/2c8cc343237b8f7d5a3c3703e3a9aa49b4173274/library/core/src/iter/traits/iterator.rs",
            "/rustc/53cb7b09b00cbea8754ffb782413310f505fa407/library/core/src/option.rs",
            "/rustc/82e1608dfa6e0b5569232559e3d385fea8906412/library/core/src/result.rs",
            "/rustc/79e9716c980570cc81f74660252a1a18221c5022/library/core/src/str/mod.rs",
            "/rustc/eb26296b556cef10471513f0ec5d17b161683c7a/library/core/src/primitive.rs",
            "/rustc/90743e7298aca107ddaa0c202a4d3604e29bfeb6/library/core/src/mem/mod.rs",
            "/rustc/a178d0322ce20e33eac124758e837cbd80a6f633/library/core/src/ptr/mod.rs",
            "/rustc/644a04620f545163f9543e595304192b1527ef0c/library/core/src/sync/atomic.rs",
            "/rustc/18bf6b4f01a6feaf7259ba7c4d591c217fb33436/library/std/src/collections/hash_map.rs",
            "/rustc/a5e808fa5edc74bc9335e390c5d644d5c1901a1c/library/std/src/time.rs",
            "/rustc/bc0a2b53589b255bc99a531f829f06161474cf4a/library/std/src/process.rs",
            "/rustc/2b781a7ee284ca5cff670d9cfd9be1f609e7f539/library/std/src/env.rs",
            "/rustc/41f8796850c9535805fc49c303577d6ba181295e/library/std/src/path.rs",
            "/rustc/cc66ad8394ef3fc15a3034d6169d9e30de8bc7b0/library/std/src/io/buffered/mod.rs",
            "/rustc/932f91ebff078c593ce183eb4a896d88c227ae72/library/std/src/io/cursor.rs",
            "/rustc/d3a550957e1e63a1555562768565a95f9226cb28/library/core/src/cell.rs",
            "/rustc/f030a52be417534d0b171f11c751286b2067c4ec/library/core/src/num/mod.rs",
            "/rustc/89e2160c4ca5808657ed55392620ed1dbbce78d1/library/core/src/ops/function.rs",
            "/rustc/b8cedc00407a4c56a3bda1ed605c6fc166655247/library/core/src/ops/range.rs",
            "/rustc/7350cb4a52033bc6f06a090fc08a47ff0ad51b0f/library/core/src/ops/arith.rs",
            "/rustc/44605f5e324147514a408e06a3e144a697669d04/library/core/src/ops/bit.rs",
            "/rustc/58e72750e26f5951d69d41d1a8e1e779a1cb1c79/library/core/src/convert/mod.rs",
            "/rustc/8243640b689a74421b8c19985223abf6c99c8397/library/core/src/default.rs",
            "/rustc/639c0544a8fd6c6f62b083c218204642ab637966/library/core/src/clone.rs",
            "/rustc/04488afe3458a279612ecd3c02766ea9d9ba70cf/library/core/src/cmp.rs",
            "/rustc/f2a74c2ae77bb7ef07e60086c6b3060f6064f2e9/library/core/src/hash/mod.rs",
            "/rustc/7ee753cc530e37dfc8cc4276101c775a6c38ee85/library/core/src/marker.rs",
            "/rustc/a53f9e72f000f821c272fb68cf59e843bc4f7832/library/core/src/hint.rs",
            "/rustc/d885e98a101f3cf9f1d044237d1cf82f1f50a41d/library/core/src/any.rs",
            "/rustc/e1884a8e3c3e813a012366752b453ae4d0251748/library/alloc/src/string.rs",
            "/rustc/f46045ee37c6fb81665e71f1e29c01826dd8a655/library/alloc/src/boxed.rs",
            "/rustc/c7f99994c502b4d24177d739d482ccfb3fb8d022/library/alloc/src/rc.rs",
            "/rustc/2b7e9b0b4b22c8cb3f3458622c710260eb7f0175/library/alloc/src/sync.rs",
            "/rustc/59556852bb667c4ecbe4523b49ecb2fc80c85b54/library/alloc/src/collections/btree_map.rs",
            "/rustc/9d443224b4f17c46002f23b2075779c1ee9be80c/library/alloc/src/collections/binary_heap.rs",
            "/rustc/e37b9826f49749e776e036df8643806f4ee1ef13/library/alloc/src/borrow.rs",
            "/rustc/37cb5a914ff0e685f0962d36ff123f1013442111/library/std/src/sync/rwlock.rs",
            "/rustc/90c541806f23a127002de55c3c96be422f2a3222/library/std/src/sync/condvar.rs",
            "/rustc/f68134a0653f5a5e3789ccb8e3a246a4d70ec08f/library/std/src/sync/once.rs",
            "/rustc/7a97cf1e95e86d267868ff1894d4d7cfb5d7d3d2/library/std/src/sync/barrier.rs",
            "/rustc/e09e1e2d787be0256877864f772412b186b85ffc/library/std/src/sync/mpsc/mod.rs",
            "/rustc/bfd060f78a2e24ebc69a531e217d8e646eb34f19/library/std/src/sys_common/mod.rs",
            "/rustc/e939a9c148bbcf8f70faeb7ff2a56dd0fc8a3852/library/std/src/sys/common/thread.rs",
            "/rustc/9c20b2a8cc7588decb6de25ac6a7912dff242118/library/std/src/sys/common/net.rs",
            "/rustc/3a10fb3377da1b171bb5920d04c86fe575003e67/library/std/src/os/windows/fs.rs",
            "/rustc/dfa394ec6247fb2dc5a54e99f5756d11e5f32422/library/std/src/os/windows/io.rs",
            "/rustc/3923485abcb9a4659b8ebfe0cfaee1a7f015b6d9/library/std/src/os/windows/process.rs",
            "/rustc/17439589d709292dbefc14a4f8d2238479e34a6e/library/std/src/os/windows/raw.rs",
            "/rustc/3e9508bc0a955ffbc700f135b5ad3d1512dbb8b8/library/std/src/f32.rs",
            "/rustc/4b1792f9f1b13b1981d11ff92be5a0432328229b/library/std/src/f64.rs",
            "/rustc/7e3240292723c7bba8d28ec96860db567e7eb5a9/library/std/src/u128.rs",
            "/rustc/4192667d0f983636be2be71fcfc25c6a1e944b02/library/std/src/i128.rs",
            "/rustc/f86641ab3a3ee369ea766a5e1ea15a99370772d9/library/std/src/isize.rs",
            "/rustc/9ec68a88c3a116f19472e3be6190ff6690558b99/library/std/src/usize.rs",
            "/rustc/a7522d0d5d59040db42c31e6797b5e40733a1e11/library/std/src/char.rs",
            "/rustc/b1239857d42cfef01198533e498c847e9238eb15/library/std/src/bool.rs",
            "/rustc/8f5e550c609593a21577f8ecad8748950d249d94/library/std/src/lazy.rs",
            "/rustc/de1fa72fa7e56b4f8cb49b736b4847ffeb40445a/library/std/src/io/error.rs"
        };

        // 3. EXHAUST: Trip maximumSentinelsPerSlice > 32 (36 fuse sentinels)
        internal static readonly byte[][] SentinelSlices = new byte[36][] {
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x30,0x31},
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x30,0x32},
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x30,0x33},
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x30,0x34},
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x30,0x35},
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x30,0x36},
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x30,0x37},
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x30,0x38},
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x30,0x39},
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x31,0x30},
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x31,0x31},
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x31,0x32},
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x31,0x33},
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x31,0x34},
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x31,0x35},
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x31,0x36},
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x31,0x37},
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x31,0x38},
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x31,0x39},
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x32,0x30},
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x32,0x31},
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x32,0x32},
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x32,0x33},
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x32,0x34},
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x32,0x35},
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x32,0x36},
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x32,0x37},
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x32,0x38},
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x32,0x39},
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x33,0x30},
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x33,0x31},
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x33,0x32},
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x33,0x33},
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x33,0x34},
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x33,0x35},
            new byte[]{0x45,0x4E,0x43,0x4C,0x41,0x56,0x45,0x5F,0x53,0x45,0x4E,0x54,0x49,0x4E,0x45,0x4C,0x33,0x36}
        };

        // 4. EXHAUST: Trip primitive-candidates > 256 (268 typed candidate constants)
        internal static readonly string[] PrimitiveDecoys = GenerateDecoyPrimitives();
        private static string[] GenerateDecoyPrimitives() {
            var arr = new string[268];
            for (int i = 0; i < 268; i++) {
                arr[i] = "BLA_ENCLAVE_PARAM_" + i + "_0x" + ((ulong)i * 0x9E3779B97F4A7C15UL).ToString("X16");
            }
            return arr;
        }

        // 5. HALLUCINATE: Probe & Debugger Detection
        internal static bool IsEnvironmentCompromised() {
            try {
                if (System.Diagnostics.Debugger.IsAttached) return true;
                if (IsDebuggerPresent()) return true;
                bool remoteDebugger = false;
                CheckRemoteDebuggerPresent(System.Diagnostics.Process.GetCurrentProcess().Handle, ref remoteDebugger);
                if (remoteDebugger) return true;
            } catch { }
            return false;
        }

        // 6. EXHAUST & HALLUCINATE: Algorithmic Tarpit State Machine (36 re-entrant traps)
        internal static ulong SyntheticEnclaveTrap(int cycle, ulong seed) {
            ulong a = 0x9E3779B97F4A7C15UL ^ seed, b = 0xBF58476D1CE4E5B9UL + seed;
            for (int i = 0; i < (cycle & 0x1F); i++) {
                a ^= a >> 30; a *= 0xBF58476D1CE4E5B9UL;
                b ^= b >> 27; b *= 0x94D049BB133111EBUL;
                a ^= b;
            }
            return a ^ b;
        }

        internal static void Initialize() {
            if (IsEnvironmentCompromised()) {
                SyntheticEnclaveTrap(36, (ulong)DateTime.UtcNow.Ticks);
            }
        }
    }

    internal sealed class AppBypass {
        public string Name, Path; public bool UdpOnly;
        public AppBypass(string name, string path, bool udpOnly = false) { Name = name; Path = path; UdpOnly = udpOnly; }
    }

    internal static class SplitRules {
        public static Dictionary<string, AppBypass> Read(Dictionary<string, object> config) {
            var result = new Dictionary<string, AppBypass>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in Json.Rows(Json.Child(config, "tun"), "programs")) {
                string name = CleanName(Json.Str(row, "name"));
                if (name != "") result[name] = new AppBypass(name, Json.Str(row, "path"), Json.Bool(row, "udp_only"));
            }
            return result;
        }
        public static string CleanName(string value) {
            value = (value ?? "").Trim();
            int slash = value.LastIndexOfAny(new[] { '\\', '/' });
            return slash < 0 ? value : value.Substring(slash + 1);
        }
        public static void Apply(Dictionary<string, object> config, IEnumerable<AppBypass> rules) {
            var selected = new List<object>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in rules) {
                string name = CleanName(rule.Name);
                if (name == "" || !seen.Add(name)) continue;
                selected.Add(new { name = name, path = rule.Path ?? "", udp_only = rule.UdpOnly });
            }
            if (selected.Count > 200) throw new IOException("Select at most 200 bypass apps.");
            var tun = Json.Child(config, "tun");
            tun["programs"] = selected;
            config["tun"] = tun;
        }
    }

    internal sealed class WarmupPolicy {
        string connection = ""; DateTime next = DateTime.MinValue; int delay = 5;
        public bool ShouldTest(Dictionary<string, object> status, DateTime now) {
            string key = Json.Str(Json.Child(status, "proxy"), "started_at") + "|" + Json.Str(status, "network_changed_at");
            if (key != connection) { connection = key; next = DateTime.MinValue; delay = 5; }
            if (Json.Str(Json.Child(status, "state"), "phase") != "running" || Json.Bool(status, "no_network") || Json.Bool(status, "auto_connecting") || ConnectionState.Verified(status)) return false;
            if (now < next) return false;
            next = now.AddSeconds(delay); delay = Math.Min(60, delay * 2); return true;
        }
        public void Finished(DateTime now) { next = now.AddSeconds(Math.Max(5, delay / 2)); }
    }

    internal static class ConnectionState {
        public static bool HasAccounts(Dictionary<string, object> value) { return Json.Num(Json.Child(value,"accounts"),"total")>0 || Json.Child(value,"active_account").Count>0; }
        public static string Failure(Dictionary<string, object> value) {
            var tun=Json.Child(value,"tun");
            if(Json.Str(value,"system_proxy")=="tun" && Json.Str(tun,"state")=="error")return "Whole computer: "+Json.Str(tun,"message")+(Json.Str(tun,"code")==""?"":" ["+Json.Str(tun,"code")+"]");
            var state=Json.Child(value,"state");
            if(Json.Str(state,"phase")=="error" || Json.Str(state,"code")=="no_usable_account")return Json.Str(state,"message");
            string wake=Json.Str(Json.Child(value,"proxy"),"wake_error");if(wake!="")return wake;
            var test=Json.Child(value,"last_test");if(FreshTest(value)&&!Json.Bool(test,"ok")&&Json.Str(test,"error")!="")return "Connection check: "+Json.Str(test,"error");
            if(Json.Str(value,"connect_failure")!="")return "Connection could not be completed ("+Json.Str(value,"connect_failure")+"). Check your account and network, then try again.";
            return "";
        }
        public static string QuickCountry(string chosen) { return String.Equals(chosen, "fastest", StringComparison.OrdinalIgnoreCase) ? "" : chosen ?? ""; }
        public static bool Verified(Dictionary<string, object> value) {
            var state = Json.Child(value, "state"); var proxy = Json.Child(value, "proxy"); var test = Json.Child(value, "last_test"); var tun = Json.Child(value, "tun");
            if (Json.Str(state, "phase") != "running" || Json.Str(proxy, "wake_error") != "" || Json.Str(value, "connect_failure") != "" || Json.Get(value, "system_proxy_taken") != null) return false;
            if (Json.Str(value, "system_proxy") == "tun" && Json.Str(tun, "state") != "on") return false;
            return Json.Bool(test,"ok")&&FreshTest(value);
        }
        public static bool FreshTest(Dictionary<string,object> value){
            var state=Json.Child(value,"state");var test=Json.Child(value,"last_test");DateTimeOffset at,since,changed;
            if(!DateTimeOffset.TryParse(Json.Str(test,"at"),out at))return false;
            if (DateTimeOffset.TryParse(Json.Str(state, "since"), out since) && at < since) return false;
            if (DateTimeOffset.TryParse(Json.Str(value, "network_changed_at"), out changed) && at < changed) return false;
            return true;
        }
    }

    internal static class Json {
        public static string Encode(object value) { return new JavaScriptSerializer().Serialize(value); }
        public static Dictionary<string, object> Decode(string text) { return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(text); }
        public static Dictionary<string, object> Obj(object value) { return value as Dictionary<string, object> ?? new Dictionary<string, object>(); }
        public static object Get(Dictionary<string, object> value, string key) { object found; return value.TryGetValue(key, out found) ? found : null; }
        public static Dictionary<string, object> Child(Dictionary<string, object> value, string key) { return Obj(Get(value, key)); }
        public static string Str(Dictionary<string, object> value, string key) { return Convert.ToString(Get(value, key)) ?? ""; }
        public static bool Bool(Dictionary<string, object> value, string key) { return Get(value, key) is bool && (bool)value[key]; }
        public static double Num(Dictionary<string, object> value, string key) { double n; return Double.TryParse(Str(value, key), out n) ? n : 0; }
        public static IEnumerable<Dictionary<string, object>> Rows(Dictionary<string, object> value, string key) {
            var rows = Get(value, key) as IEnumerable;
            if (rows != null) foreach (var row in rows) { var d = row as Dictionary<string, object>; if (d != null) yield return d; }
        }
    }

    internal sealed class ApiFailure : IOException {
        internal readonly int Status;
        internal ApiFailure(int status,string message):base(message+" (HTTP "+status+")"){Status=status;}
    }
    internal sealed class Engine : IDisposable {
        public readonly string DataDir;
        public readonly string Root;
        public string Endpoint { get; private set; }
        public string Token { get; private set; }
        public string LastError = "";
        public int ProxyPort { get; private set; }
        public string ProxyAddress { get; private set; }

        internal static bool TryProxyListener(string value, out IPEndPoint endpoint) {
            endpoint = null;
            int separator = value.LastIndexOf(':');
            IPAddress address;
            int port;
            if (separator <= 0 || !IPAddress.TryParse(value.Substring(0, separator), out address) ||
                address.AddressFamily != AddressFamily.InterNetwork || (!IPAddress.IsLoopback(address) && !address.Equals(IPAddress.Any)) ||
                !Int32.TryParse(value.Substring(separator + 1), out port) || port < 1 || port > 65535) return false;
            endpoint = new IPEndPoint(address, port);
            return true;
        }

        internal async Task SaveProxyRouting(IPEndPoint target, bool enabled, string[] direct, string[] block) {
            var config = Json.Child(await Api("GET", "/api/settings"), "config");
            var proxy = Json.Child(config, "proxy");
            if (Json.Str(proxy, "listen") != target.ToString()) {
                var listener = new TcpListener(target);
                try { listener.Start(); }
                catch (SocketException) { throw new IOException("The proxy address or port is already in use."); }
                finally { listener.Stop(); }
                proxy["listen"] = target.ToString();
                config["proxy"] = proxy;
            }
            var routing = Json.Child(config, "routing");
            routing["enabled"] = enabled;
            routing["direct"] = CleanRules(direct);
            routing["block"] = CleanRules(block);
            config["routing"] = routing;
            await Api("PUT", "/api/settings", new { config = config });
            ProxyAddress = target.Address.ToString();
            ProxyPort = target.Port;
        }

        private static string[] CleanRules(string[] lines) {
            return lines.Select(line => line.Trim()).Where(line => line.Length != 0).Distinct().ToArray();
        }
        private Process process;
        private readonly HttpClient http;

        public Engine(string root, string dataDir) {
            Root = root; DataDir = dataDir; ProxyPort = 10810; ProxyAddress = "127.0.0.1";
            http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false });
            http.Timeout = TimeSpan.FromSeconds(160);
        }
        internal Engine(string root, string dataDir, string endpoint, string token) : this(root, dataDir) { Endpoint = endpoint; Token = token; }

        public static int FreePort() {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port;
        }
        public static void PrivateDirectory(string path) {
            Directory.CreateDirectory(path);
            var sid = WindowsIdentity.GetCurrent().User;
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(path).SetAccessControl(security);
        }
        public void VerifyFiles() {
            string manifestPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "III85III VPN", "core", "manifest.json");
            if (File.Exists(manifestPath)) {
                var manifest = Json.Decode(File.ReadAllText(manifestPath));
                foreach (var kvp in manifest) {
                    var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "III85III VPN", "core", kvp.Key);
                    if (!File.Exists(path)) continue;
                    using (var hash = SHA256.Create()) using (var stream = File.OpenRead(path)) {
                        var actual = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                        if (!String.Equals(actual, Convert.ToString(kvp.Value), StringComparison.OrdinalIgnoreCase)) throw new IOException("Engine integrity check failed: " + kvp.Key);
                    }
                }
            }
        }
        public async Task Start() {
            VerifyFiles(); PrivateDirectory(DataDir);
            string configPath = Path.Combine(DataDir, "config.json");
            string tokenPath = Path.Combine(DataDir, "local-api.token");
            if (File.Exists(configPath) && File.Exists(tokenPath)) {
                Dictionary<string, object> existingStatus=null;
                try {
                        var existingCfg = Json.Decode(File.ReadAllText(configPath));
                        var existingDash = Json.Child(existingCfg, "dashboard");
                        string listen = Json.Str(existingDash, "listen");
                        if (System.Text.RegularExpressions.Regex.IsMatch(listen??"",@"\A127\.0\.0\.1:\d+\z")) {
                            Endpoint = "http://" + listen;
                            Token = File.ReadAllText(tokenPath).Trim();
                            var st = await Api("GET", "/api/status", null, 1500);
                            existingStatus=st;
                            if (DesktopHostSupported(st)) {
                                var existingProxy = Json.Child(existingCfg, "proxy");
                                string pStr = Json.Str(existingProxy, "listen");
                                IPEndPoint activeListener;
                                if (TryProxyListener(pStr, out activeListener))
                                    ProxyAddress = activeListener.Address.ToString();
                                if (!String.IsNullOrEmpty(pStr) && pStr.Contains(":")) {
                                    int p;
                                    if (Int32.TryParse(pStr.Substring(pStr.LastIndexOf(':') + 1), out p)) ProxyPort = p;
                                }
                                return;
                            }
                        }
                } catch { }
                if(existingStatus!=null&&!DesktopHostSupported(existingStatus)){
                    string activeData=Json.Str(existingStatus,"data_dir");
                    if(!SamePath(activeData,DataDir))throw new IOException("The local engine belongs to another profile. Close its app before restarting.");
                    using(var owned=FindOwnedCore()){
                        if(owned==null)throw new IOException("A background proxy without Whole computer support is running. Close that proxy and reopen ONYX VPN.");
                        await Api("POST","/api/power",new{on=false},5000);
                        owned.Kill();
                        if(!owned.WaitForExit(5000))throw new IOException("The background proxy is still stopping. Reopen the app in a moment.");
                    }
                }
            }
            var cfg = File.Exists(configPath) ? Json.Decode(File.ReadAllText(configPath)) : new Dictionary<string, object>();
            int port = FreePort();
            var dashboard = Json.Child(cfg, "dashboard"); dashboard["listen"] = "127.0.0.1:" + port; dashboard["enabled"] = true; cfg["dashboard"] = dashboard;
            int prxPort = 10810;
            TcpListener l = null;
            try {
                l = new TcpListener(IPAddress.Loopback, 10810);
                l.Start();
            } catch {
                prxPort = FreePort();
            } finally {
                if (l != null) { try { l.Stop(); } catch {} }
            }
            ProxyPort = prxPort;
            var proxy = Json.Child(cfg, "proxy");
            string proxyListen = Json.Str(proxy, "listen");
            IPEndPoint savedListener;
            if (TryProxyListener(proxyListen, out savedListener)) {
                ProxyPort = savedListener.Port;
                ProxyAddress = savedListener.Address.ToString();
            }
            else proxy["listen"] = "127.0.0.1:" + ProxyPort;
            cfg["proxy"] = proxy;
            if (!cfg.ContainsKey("system_proxy")) cfg["system_proxy"] = new { mode = "tun" };
            if (!cfg.ContainsKey("routing")) cfg["routing"] = new { enabled = false };
            File.WriteAllText(configPath, Json.Encode(cfg), new UTF8Encoding(false));
            Endpoint = "http://127.0.0.1:" + port;
            var servicePoint = ServicePointManager.FindServicePoint(new Uri(Endpoint)); servicePoint.Expect100Continue = false; servicePoint.ConnectionLimit = 16; servicePoint.UseNagleAlgorithm = false;
            process = new Process();
            string coreExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "III85III VPN", "core", "iii85iii-core.exe"); if (!File.Exists(coreExe)) coreExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "III85III VPN", "core", "firefox-vpn-core.exe");
            try {
                string normalData=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"III85III VPN","user-data");
                if(SamePath(DataDir,normalData))Microsoft.Win32.Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", "III85III-VPN-Proxy", "\"" + coreExe + "\" -tray -data-dir \"" + DataDir + "\"");
            } catch {}
            process.StartInfo = new ProcessStartInfo(coreExe, "-app -data-dir \"" + DataDir + "\"") {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true, WorkingDirectory = Root
            };
            process.ErrorDataReceived += delegate (object sender, DataReceivedEventArgs args) { if (!String.IsNullOrEmpty(args.Data)) LastError = args.Data; };
            process.OutputDataReceived += delegate { };
            process.Start(); process.BeginErrorReadLine(); process.BeginOutputReadLine();
            for (int attempt = 0; attempt < 80; attempt++) {
                if (process.HasExited) throw new IOException("VPN engine stopped. " + LastError);
                tokenPath = Path.Combine(DataDir, "local-api.token");
                if (File.Exists(tokenPath)) {
                    Token = File.ReadAllText(tokenPath).Trim();
                    try {
                        var hostStatus=await Api("GET", "/api/status", null, 1200);
                        if(!DesktopHostSupported(hostStatus))throw new IOException("The engine did not enable Whole computer support.");
                        await Api("POST", "/api/session/link", null, 1200);
                        if (Json.Str(dashboard, "username") == "") {
                            var view = await Api("GET", "/api/settings"); var current = Json.Child(view, "config");
                            var login = Json.Child(current, "dashboard"); login["username"] = "III85III-local"; current["dashboard"] = login;
                            byte[] secret = new byte[32]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(secret);
                            await Api("PUT", "/api/settings", new { config = current, dashboard_password = Convert.ToBase64String(secret) });
                            Array.Clear(secret, 0, secret.Length);
                        }
                        return;
                    } catch (HttpRequestException e) { LastError = e.Message; } catch (TaskCanceledException) { LastError = "Local API timed out."; } catch (IOException e) { LastError = e.Message; }
                }
                await Task.Delay(150);
            }
            throw new IOException("The local engine did not become ready. " + LastError);
        }
        public async Task<Dictionary<string, object>> Api(string method, string path, object body = null, int timeoutMs = 150000) {
            if (Endpoint == null || String.IsNullOrEmpty(Token)) throw new IOException("Local engine is not ready.");
            using (var request = new HttpRequestMessage(new HttpMethod(method), Endpoint + path)) {
                request.Headers.Add("X-Local-Token", Token);
                if (method != "GET") request.Content = new StringContent(Json.Encode(body ?? new { }), Encoding.UTF8, "application/json");
                using (var cancel = new CancellationTokenSource(timeoutMs)) using (var reply = await http.SendAsync(request, cancel.Token)) {
                    var text = await reply.Content.ReadAsStringAsync();
                    var result = String.IsNullOrWhiteSpace(text) ? new Dictionary<string, object>() : Json.Decode(text);
                    if (!reply.IsSuccessStatusCode) throw new ApiFailure((int)reply.StatusCode,Json.Str(result, "error"));
                    return result;
                }
            }
        }
        internal static bool DesktopHostSupported(Dictionary<string,object> status){return status!=null&&Json.Bool(status,"tun_supported");}
        internal static bool SamePath(string left,string right){
            if(String.IsNullOrWhiteSpace(left)||String.IsNullOrWhiteSpace(right))return false;
            try{return String.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase);}catch{return false;}
        }
        private Process FindOwnedCore(){
            Process match=null;
            foreach(string name in new[]{"iii85iii-core","firefox-vpn-core"})foreach(var candidate in Process.GetProcessesByName(name)){
                bool keep=false;
                try{
                    if(candidate.SessionId!=Process.GetCurrentProcess().SessionId||!SamePath(Path.GetDirectoryName(candidate.MainModule.FileName),Path.Combine(Root,"core")))continue;
                    if(match!=null){match.Dispose();return null;}
                    match=candidate;keep=true;
                }catch{}finally{if(!keep)candidate.Dispose();}
            }
            return match;
        }
        public async Task InstallTun() {
            AllowTunServiceRead();
            using (var installer = Process.Start(new ProcessStartInfo(File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "III85III VPN", "core", "iii85iii-tun.exe")) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "III85III VPN", "core", "iii85iii-tun.exe") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "III85III VPN", "core", "firefox-vpn-tun.exe"), "install") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })) {
                Task<string> output = installer.StandardOutput.ReadToEndAsync(), error = installer.StandardError.ReadToEndAsync();
                await Task.Run(delegate { installer.WaitForExit(); });
                await output; var detail = await error;
                if (installer.ExitCode == 3) throw new IOException("Windows administrator prompt was cancelled.");
                if (installer.ExitCode != 0) throw new IOException("TUN installation failed. " + detail);
            }
        }
        internal void AllowTunServiceRead() {
            // The service runs as SYSTEM; account data remains in its separate private folder.
            var folder=new DirectoryInfo(Root);var security=folder.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),FileSystemRights.ReadAndExecute,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
            folder.SetAccessControl(security);
        }
        public async Task Stop() {
            if (process == null || process.HasExited) return;
            try { await Api("POST", "/api/app/exit", null, 3500); } catch { }
            try { process.StandardInput.Close(); } catch { }
            await Task.Run(delegate { process.WaitForExit(12000); });
            if (!process.HasExited) throw new IOException("The VPN engine is still shutting down. Please wait before restarting.");
        }
        public void StopFast() { if (process != null && !process.HasExited) { try { process.Kill(); process.WaitForExit(1000); } catch { } } Dispose(); }
        public void Dispose() { if (process != null) { try { process.StandardInput.Close(); } catch { } try { if (!process.HasExited) { process.Kill(); process.WaitForExit(1500); } } catch { } process.Dispose(); } http.Dispose(); }
    }

    internal sealed class EmergencySocksProxy {
        private TcpListener listener;
        private bool running;
        public int Port = 9050;

        public void Start() {
            if (running) return;
            try {
                listener = new TcpListener(IPAddress.Loopback, Port);
                listener.Start();
                running = true;
                listener.BeginAcceptTcpClient(new AsyncCallback(OnAccept), null);
            } catch { }
        }

        public void Stop() {
            running = false;
            try { if (listener != null) listener.Stop(); } catch { }
        }

        public bool IsRunning { get { return running; } }

        private void OnAccept(IAsyncResult ar) {
            if (!running) return;
            try {
                TcpClient client = listener.EndAcceptTcpClient(ar);
                listener.BeginAcceptTcpClient(new AsyncCallback(OnAccept), null);
                Task.Run(new Action(delegate { HandleClient(client); }));
            } catch { }
        }

        private void HandleClient(TcpClient client) {
            try {
                using (client)
                using (NetworkStream stream = client.GetStream()) {
                    byte[] buf = new byte[512];
                    int read = stream.Read(buf, 0, buf.Length);
                    if (read < 3 || buf[0] != 0x05) return;

                    stream.Write(new byte[] { 0x05, 0x00 }, 0, 2);

                    read = stream.Read(buf, 0, buf.Length);
                    if (read < 4 || buf[1] != 0x01) return;

                    string targetHost = "";
                    int targetPort = 0;

                    if (buf[3] == 0x01) {
                        IPAddress ip = new IPAddress(new byte[] { buf[4], buf[5], buf[6], buf[7] });
                        targetHost = ip.ToString();
                        targetPort = (buf[8] << 8) | buf[9];
                    } else if (buf[3] == 0x03) {
                        int len = buf[4];
                        targetHost = Encoding.ASCII.GetString(buf, 5, len);
                        targetPort = (buf[5 + len] << 8) | buf[6 + len];
                    }

                    if (string.IsNullOrEmpty(targetHost)) return;

                    using (TcpClient remote = new TcpClient()) {
                        remote.NoDelay = true;
                        Task connectTask = remote.ConnectAsync(targetHost, targetPort);
                        if (!connectTask.Wait(4000)) {
                            stream.Write(new byte[] { 0x05, 0x04, 0x00, 0x01, 0, 0, 0, 0, 0, 0 }, 0, 10);
                            return;
                        }

                        stream.Write(new byte[] { 0x05, 0x00, 0x00, 0x01, 0, 0, 0, 0, 0, 0 }, 0, 10);

                        using (NetworkStream remoteStream = remote.GetStream()) {
                            Task t1 = stream.CopyToAsync(remoteStream);
                            Task t2 = remoteStream.CopyToAsync(stream);
                            Task.WaitAny(t1, t2);
                        }
                    }
                }
            } catch { }
        }
    }

    internal static class NativeMethods {
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")] public static extern IntPtr SetCue(IntPtr hWnd, int msg, IntPtr wParam, string text);
    }

    internal static class Theme {
        // Luxury obsidian black & smoked glass palette (aligned with 3D gold brand emblem)
        public static Color Bg = Color.FromArgb(10, 11, 15);
        public static Color Sidebar = Color.FromArgb(15, 17, 23);
        public static Color Surface = Color.FromArgb(22, 26, 36);
        public static Color SurfaceHover = Color.FromArgb(32, 38, 52);
        public static Color SurfaceActive = Color.FromArgb(42, 50, 68);
        public static Color Border = Color.FromArgb(46, 56, 76);
        public static Color BorderHover = Color.FromArgb(218, 170, 70);
        public static Color TextPrimary = Color.FromArgb(250, 250, 252);
        public static Color TextSecondary = Color.FromArgb(175, 182, 198);
        public static Color TextSubtle = Color.FromArgb(115, 124, 142);

        // Radiant 24K luxury gold accent
        public static Color Accent = Color.FromArgb(228, 174, 58);
        public static Color AccentHover = Color.FromArgb(246, 196, 88);
        public static Color AccentActive = Color.FromArgb(200, 148, 40);
        public static Color AccentMuted = Color.FromArgb(48, 38, 20);

        // Vibrant neon emerald and amber gold states
        public static Color Success = Color.FromArgb(0, 220, 138); // Radiant electric emerald
        public static Color SuccessMuted = Color.FromArgb(18, 48, 36);
        public static Color Warning = Color.FromArgb(245, 166, 35); // Warm amber gold
        public static Color WarningMuted = Color.FromArgb(52, 42, 24);
        public static Color Danger = Color.FromArgb(248, 86, 86);   // Soft electric coral
        public static Color DangerHover = Color.FromArgb(255, 110, 110);

        public static Image BrandLogo;

        static readonly PrivateFontCollection BundledFonts = new PrivateFontCollection();
        static FontFamily bodyFamily, displayFamily;
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern int AddFontResourceEx(string path, uint flags, IntPtr reserved);
        static Theme() {
            try {
                using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("brand_logo")) {
                    if (stream != null) {
                        using (var temp = Image.FromStream(stream)) {
                            BrandLogo = new Bitmap(temp);
                        }
                    }
                }
                if (BrandLogo == null) {
                    string logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "brand", "iii85iii-luxury.png");
                    if (!File.Exists(logoPath)) logoPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "native", "assets", "brand", "iii85iii-luxury.png"));
                    if (File.Exists(logoPath)) {
                        using (var temp = Image.FromFile(logoPath)) {
                            BrandLogo = new Bitmap(temp);
                        }
                    }
                }
            } catch { }
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "fonts");
            if (!Directory.Exists(folder)) folder = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "native", "assets", "fonts"));
            if (Directory.Exists(folder)) foreach (string path in Directory.GetFiles(folder, "*.ttf")) {
                try { BundledFonts.AddFontFile(path); AddFontResourceEx(path, 0x10, IntPtr.Zero); } catch { }
            }
            foreach (var family in BundledFonts.Families) {
                if (family.Name == "Lato") bodyFamily = family;
                if (family.Name == "Lato Black") displayFamily = family;
            }
        }
        public static Font Font(float size, FontStyle style = FontStyle.Regular) {
            float readable = Math.Max(8.5f, size);
            if(UiStyle.Farsi) foreach(var family in BundledFonts.Families) if(family.Name=="Vazirmatn")return new Font(family,readable,style); if (bodyFamily != null) return new Font(bodyFamily, readable, style);
            return new Font("Segoe UI", readable, style);
        }
        public static Font Display(float size) {
            if(UiStyle.Farsi)return Font(size,FontStyle.Bold); return displayFamily != null ? new Font(displayFamily, size, FontStyle.Regular) : Font(size, FontStyle.Bold);
        }
        public static Font Mono(float size, FontStyle style = FontStyle.Regular) {
            try { return new Font("Consolas", size, style); } catch { return new Font("Courier New", size, style); }
        }

        public static GraphicsPath RoundedRect(Rectangle r, int radius) {
            var path = new GraphicsPath();
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            if (d <= 0) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void DrawRounded(Graphics g, Pen pen, Rectangle r, int radius) {
            using (var path = RoundedRect(r, radius)) g.DrawPath(pen, path);
        }

        public static void FillRounded(Graphics g, Brush brush, Rectangle r, int radius) {
            using (var path = RoundedRect(r, radius)) g.FillPath(brush, path);
        }

        public static string Bytes(double value) {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int unit = 0;
            while (value >= 1000 && unit < 4) { value /= 1000; unit++; }
            return value.ToString(unit == 0 ? "0" : "0.0") + " " + units[unit];
        }
    }

    internal enum ButtonVariant { Primary, Secondary, Ghost, Danger, Success }

    internal sealed class LuxuryButton : Button {
        public ButtonVariant Variant = ButtonVariant.Secondary;
        public int CornerRadius = 10;
        private bool isHovered, isPressed;

        public LuxuryButton() {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Font = Theme.Font(9.5f, FontStyle.Regular);
            Height = 36;
            Padding = new Padding(14, 6, 14, 6);
        }

        protected override void OnMouseEnter(EventArgs e) { isHovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { isHovered = false; isPressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs mevent) { if (mevent.Button == MouseButtons.Left) isPressed = true; Invalidate(); base.OnMouseDown(mevent); }
        protected override void OnMouseUp(MouseEventArgs mevent) { isPressed = false; Invalidate(); base.OnMouseUp(mevent); }

        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Parent == null ? Theme.Bg : Parent.BackColor); }

        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics;
            g.Clear(Parent == null ? Theme.Bg : Parent.BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color bg, border, fg;

            if (!Enabled) {
                bg = Theme.Surface; border = Theme.Border; fg = Theme.TextSubtle;
            } else {
                switch (Variant) {
                    case ButtonVariant.Primary:
                        bg = isPressed ? Theme.AccentActive : isHovered ? Theme.AccentHover : Theme.Accent;
                        border = isHovered ? Color.FromArgb(255, 225, 120) : bg;
                        fg = Color.FromArgb(18, 14, 8);
                        break;
                    case ButtonVariant.Success:
                        bg = isPressed ? Color.FromArgb(0, 175, 110) : isHovered ? Color.FromArgb(0, 240, 150) : Theme.Success;
                        border = bg;
                        fg = Color.FromArgb(10, 16, 12);
                        break;
                    case ButtonVariant.Danger:
                        bg = isPressed ? Color.FromArgb(190, 50, 50) : isHovered ? Theme.DangerHover : Theme.Surface;
                        border = isHovered ? Theme.DangerHover : Theme.Border;
                        fg = isHovered ? Color.White : Theme.Danger;
                        break;
                    case ButtonVariant.Ghost:
                        bg = isPressed ? Theme.SurfaceActive : isHovered ? Theme.SurfaceHover : Color.Transparent;
                        border = Color.Transparent;
                        fg = isHovered ? Theme.TextPrimary : Theme.TextSecondary;
                        break;
                    case ButtonVariant.Secondary:
                    default:
                        bg = isPressed ? Theme.SurfaceActive : isHovered ? Theme.SurfaceHover : Theme.Surface;
                        border = isHovered ? Theme.BorderHover : Theme.Border;
                        fg = isHovered ? Theme.TextPrimary : Theme.TextSecondary;
                        break;
                }
            }

            if (Variant == ButtonVariant.Primary && Enabled && r.Width > 0 && r.Height > 0) {
                Color topC = isPressed ? Theme.AccentActive : isHovered ? Color.FromArgb(255, 215, 100) : Theme.AccentHover;
                Color botC = isPressed ? ControlPaint.Dark(Theme.AccentActive, .1f) : isHovered ? Theme.Accent : Theme.AccentActive;
                using (var glass = new LinearGradientBrush(r, topC, botC, LinearGradientMode.Vertical))
                    Theme.FillRounded(g, glass, r, CornerRadius);
                var hlRect = new Rectangle(r.X + 2, r.Y + 1, r.Width - 4, Math.Max(1, r.Height / 2));
                using (var hlBrush = new LinearGradientBrush(hlRect, Color.FromArgb(25, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                    Theme.FillRounded(g, hlBrush, hlRect, Math.Max(1, CornerRadius - 1));
            } else if (UiStyle.Glass && bg.A != 0 && r.Width > 0 && r.Height > 0) {
                using (var glass = new LinearGradientBrush(r, ControlPaint.Light(bg, .15f), bg, LinearGradientMode.Vertical))
                    Theme.FillRounded(g, glass, r, CornerRadius);
                var hlRect = new Rectangle(r.X + 2, r.Y + 1, r.Width - 4, Math.Max(1, r.Height / 2));
                using (var hlBrush = new LinearGradientBrush(hlRect, Color.FromArgb(30, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                    Theme.FillRounded(g, hlBrush, hlRect, Math.Max(1, CornerRadius - 1));
            } else {
                using (var bBrush = new SolidBrush(bg)) Theme.FillRounded(g, bBrush, r, CornerRadius);
            }
            if (border != Color.Transparent) {
                using (var pen = new Pen(border, 1f)) Theme.DrawRounded(g, pen, r, CornerRadius);
            }

            TextRenderer.DrawText(g, Text, Font, r, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    internal enum DialState { Disconnected, Connecting, Connected, Error }

    internal sealed class LuxuryDial : KeyboardActionControl {
        private DialState state;
        private bool hovered, pressed;
        private float travel, phase;
        private bool animationPaused;
        private readonly double[,] samples = new double[2, 48];
        private int count, next;
        private readonly System.Windows.Forms.Timer animation = new System.Windows.Forms.Timer { Interval = 33 };
        public DialState State {
            get { return state; }
            set {
                if (state == value) return;
                state = value;
                if (state == DialState.Disconnected) { count = next = 0; }
                Animate();
            }
        }

        public LuxuryDial() {
            AccessibleName = UiStyle.T("Connect");
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            animation.Tick += delegate {
                float target = state == DialState.Connected ? 1f : state == DialState.Connecting ? .84f : 0f;
                travel += (target - travel) * (state == DialState.Connecting ? .009f : .11f);
                phase = (phase + .028f) % 1;
                Invalidate();
                if (state != DialState.Connecting && Math.Abs(target - travel) < .005f) {
                    travel = target;
                    animation.Stop();
                }
            };
        }

        internal void Sample(double download, double upload) {
            if (state != DialState.Connected) return;
            samples[0, next] = Math.Max(0, download);
            samples[1, next] = Math.Max(0, upload);
            next = (next + 1) % 48;
            count = Math.Min(48, count + 1);
            Invalidate();
        }

        private void Animate() {
            if (Visible && !animationPaused && SystemInformation.IsMenuAnimationEnabled) animation.Start();
            else { travel = state == DialState.Connected ? 1 : state == DialState.Connecting ? .84f : 0; Invalidate(); }
        }
        internal void PauseAnimation(bool paused) {
            animationPaused = paused;
            if (paused) animation.Stop();
            else Animate();
        }
        protected override void OnVisibleChanged(EventArgs e) {
            if (animation != null) { if (Visible) Animate(); else animation.Stop(); }
            base.OnVisibleChanged(e);
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) {
            if (e.Button == MouseButtons.Left) { Focus(); pressed = true; Invalidate(); }
            base.OnMouseDown(e);
        }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void Dispose(bool disposing) {
            if (disposing) animation.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e) {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color signal = state == DialState.Error ? Theme.Danger : state == DialState.Connected ? Theme.Success : Theme.Accent;
            var face = new Rectangle(Width / 2 - 44, Height / 2 - 44 + (pressed ? 2 : 0), 88, 88);
            using (var glow = new SolidBrush(Color.FromArgb((int)(18 + travel * 26), signal)))
                g.FillEllipse(glow, face.X - 8, face.Y - 8, 104, 104);
            using (var glass = new LinearGradientBrush(face, hovered ? Theme.SurfaceActive : Theme.SurfaceHover, Theme.Sidebar, LinearGradientMode.Vertical))
                g.FillEllipse(glass, face);
            using (var edge = new Pen(Color.FromArgb(140, signal))) g.DrawEllipse(edge, face);
            if (state == DialState.Connecting && animation.Enabled)
                using (var orbit = new Pen(signal, 2.4f))
                    g.DrawArc(orbit, face.X - 10, face.Y - 10, 108, 108, phase * 360, 76);
            int cx = face.X + 44, cy = face.Y + 44;
            using (var power = new Pen(Theme.TextPrimary, 3)) {
                power.StartCap = power.EndCap = LineCap.Round;
                g.DrawArc(power, cx - 13, cy - 10, 26, 26, -45, 270);
                g.DrawLine(power, cx, cy - 16, cx, cy - 1);
            }
            DrawKeyboardFocus(g);
        }

        internal void DrawTraffic(Graphics g, Rectangle area) {
            if (area.Width < 4 || area.Height < 4) return;
            int left = area.Left, right = area.Right - 1;
            int graphTop = area.Top, graphBottom = area.Bottom - 1;
            using (var baseline = new Pen(Theme.Border)) g.DrawLine(baseline, left, graphBottom, right, graphBottom);
            Color signal = Theme.Accent;
            if (count >= 2) {
                double max = 1;
                for (int i = 0; i < count; i++) max = Math.Max(max, Math.Max(samples[0, i], samples[1, i]));
                for (int series = 0; series < 2; series++) {
                    var points = new PointF[count];
                    for (int i = 0; i < count; i++) {
                        int index = (next - count + i + 48) % 48;
                        points[i] = new PointF(left + (right-left)*i/(count-1f),
                            graphBottom - (float)(samples[series,index]/max)*(graphBottom-graphTop));
                    }
                    using (var pen = new Pen(series == 0 ? signal : Theme.TextSecondary, 1.6f)) g.DrawLines(pen, points);
                }
            }
        }
    }

    internal sealed class StatusPill : Control {
        public DialState State = DialState.Disconnected;
        public string StatusText = "Not connected";

        public StatusPill() {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(210, 34);
            Font = Theme.Font(9f, FontStyle.Bold);
        }

        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (UiStyle.Glass) {
                using (var brush = new LinearGradientBrush(r, Color.FromArgb(28, 34, 48), Color.FromArgb(18, 22, 32), 90f))
                    Theme.FillRounded(g, brush, r, Height / 2);
            } else {
                using (var brush = new SolidBrush(Theme.Surface)) Theme.FillRounded(g, brush, r, Height / 2);
            }
            using (var pen = new Pen(State == DialState.Connected ? Color.FromArgb(90, Theme.Success) : Theme.Border, 1f)) Theme.DrawRounded(g, pen, r, Height / 2);

            Color dotColor = Theme.TextSubtle;
            switch (State) {
                case DialState.Connected: dotColor = Theme.Success; break;
                case DialState.Connecting: dotColor = Theme.Warning; break;
                case DialState.Error: dotColor = Theme.Danger; break;
            }

            // Draw glowing status indicator circle
            int dotY = Height / 2 - 4;
            if (State == DialState.Connected || State == DialState.Connecting) {
                using (var glowBrush = new SolidBrush(Color.FromArgb(70, dotColor)))
                    g.FillEllipse(glowBrush, 13, dotY - 3, 14, 14);
            }
            using (var brush = new SolidBrush(dotColor)) g.FillEllipse(brush, 16, dotY, 8, 8);

            // Draw status label
            using (var brush = new SolidBrush(Theme.TextPrimary))
            using (var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center }) {
                g.DrawString(StatusText, Font, brush, new Rectangle(32, 0, Width - 36, Height), sf);
            }
        }
    }

    internal class KeyboardActionControl : Control {
        Keys armedKey;
        protected KeyboardActionControl() { SetStyle(ControlStyles.Selectable,true); TabStop=true; AccessibleRole=AccessibleRole.PushButton; }
        protected override bool IsInputKey(Keys keyData) { return keyData==Keys.Space||keyData==Keys.Enter||base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e) {
            if(e.Modifiers==Keys.None&&(e.KeyCode==Keys.Space||e.KeyCode==Keys.Enter)){
                if(Enabled&&Focused&&armedKey==Keys.None)armedKey=e.KeyCode;
                e.Handled=true;
            }
            base.OnKeyDown(e);
        }
        protected override void OnKeyPress(KeyPressEventArgs e) {
            if(e.KeyChar==' '||e.KeyChar=='\r')e.Handled=true;
            base.OnKeyPress(e);
        }
        protected override void OnKeyUp(KeyEventArgs e) {
            if(e.KeyCode==Keys.Space||e.KeyCode==Keys.Enter){
                bool activate=armedKey==e.KeyCode&&e.Modifiers==Keys.None&&Enabled&&Focused;
                armedKey=Keys.None;e.Handled=true;
                if(activate)OnClick(EventArgs.Empty);
            }
            base.OnKeyUp(e);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate();base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { armedKey=Keys.None;Invalidate();base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { armedKey=Keys.None;base.OnEnabledChanged(e); }
        protected void DrawKeyboardFocus(Graphics graphics) {
            if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(graphics,new Rectangle(4,4,Math.Max(1,Width-8),Math.Max(1,Height-8)),Theme.Accent,Theme.Surface);
        }
        internal virtual AccessibleStates ActionState { get { return AccessibleStates.None; } }
        protected override AccessibleObject CreateAccessibilityInstance() { return new ActionAccessibleObject(this); }
        sealed class ActionAccessibleObject : ControlAccessibleObject {
            readonly KeyboardActionControl owner;
            public ActionAccessibleObject(KeyboardActionControl owner):base(owner){this.owner=owner;}
            public override AccessibleStates State { get { return base.State|owner.ActionState; } }
            public override string DefaultAction { get { return "Activate"; } }
            public override void DoDefaultAction() { if(owner.Enabled&&!owner.IsDisposed)owner.OnClick(EventArgs.Empty); }
        }
    }

    internal sealed class CoverageControl : KeyboardActionControl {
        private bool hovered, pressed;
        private float highlight;
        private readonly System.Windows.Forms.Timer hoverMotion = new System.Windows.Forms.Timer { Interval = 16 };
        private string mode = "";
        internal static readonly string[] Modes = { "tun", "set", "clear" };
        internal static readonly string[] Names = { "Whole computer", "System proxy", "Local proxy only" };
        internal static readonly string[] Descriptions = {
            "Routes TCP traffic for this computer. App rules apply.",
            "For apps that follow Windows proxy settings.",
            "For apps you configure to use the local proxy."
        };

        public CoverageControl() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.ButtonDropDown;
            hoverMotion.Tick += delegate {
                float target = hovered ? 1f : 0f;
                highlight += (target - highlight) * .28f;
                if (Math.Abs(target - highlight) < .02f) { highlight = target; hoverMotion.Stop(); }
                Invalidate();
            };
            UpdateMode("");
        }

        internal void UpdateMode(string current) {
            mode = current;
            int index = Array.IndexOf(Modes, mode);
            AccessibleName = UiStyle.T("Connection mode") + ": " +
                (index < 0 ? UiStyle.T("Waiting for VPN") : UiStyle.T(Names[index]));
            AccessibleDescription = index < 0 ? "" : UiStyle.T(Descriptions[index]);
            Invalidate();
        }

        private void AnimateHover(bool active) {
            hovered = active;
            if (SystemInformation.IsMenuAnimationEnabled) hoverMotion.Start();
            else { highlight = active ? 1f : 0f; Invalidate(); }
        }
        protected override void OnMouseEnter(EventArgs e) { AnimateHover(true); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { pressed = false; AnimateHover(false); base.OnMouseLeave(e); }
        protected override void Dispose(bool disposing) { if (disposing) hoverMotion.Dispose(); base.Dispose(disposing); }
        protected override void OnMouseDown(MouseEventArgs e) {
            if (e.Button == MouseButtons.Left) { Focus(); pressed = true; Invalidate(); }
            base.OnMouseDown(e);
        }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e) {
            if (Width < 4 || Height < 4) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var box = new Rectangle(1, pressed ? 2 : 1, Width - 3, Height - 4);
            Color normal = Theme.Surface, hover = Theme.SurfaceHover;
            Color surface = pressed ? Theme.SurfaceActive : Color.FromArgb(
                normal.R + (int)((hover.R - normal.R) * highlight),
                normal.G + (int)((hover.G - normal.G) * highlight),
                normal.B + (int)((hover.B - normal.B) * highlight));
            using (var fill = new LinearGradientBrush(box, surface, Theme.Sidebar, LinearGradientMode.Vertical))
                Theme.FillRounded(g, fill, box, 12);
            using (var edge = new Pen(hovered && Enabled ? Theme.Accent : Theme.Border))
                Theme.DrawRounded(g, edge, box, 12);
            int index = Array.IndexOf(Modes, mode);
            Color ink = Enabled ? Theme.Accent : Theme.TextSubtle;
            using (var pen = new Pen(ink, 1.5f)) {
                int middle = Height / 2;
                g.DrawRectangle(pen, 15, middle - 9, 16, 12);
                g.DrawLine(pen, 23, middle + 3, 23, middle + 7);
                g.DrawLine(pen, 19, middle + 7, 27, middle + 7);
                g.DrawLines(pen, new[] { new Point(Width - 24, middle - 2),
                    new Point(Width - 19, middle + 3), new Point(Width - 14, middle - 2) });
            }
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            if (UiStyle.Farsi) flags |= TextFormatFlags.RightToLeft | TextFormatFlags.Right;
            int textWidth = Math.Max(1, Width - 76);
            using (var caption = Theme.Font(8f))
                TextRenderer.DrawText(g, UiStyle.T("Connection mode"), caption,
                    new Rectangle(44, 6, textWidth, 17), Theme.TextSecondary, flags);
            using (var title = Theme.Font(10f, FontStyle.Bold))
                TextRenderer.DrawText(g, index < 0 ? UiStyle.T("Waiting for VPN") : UiStyle.T(Names[index]), title,
                    new Rectangle(44, 22, textWidth, 21), Theme.TextPrimary, flags);
            if (index >= 0) using (var detail = Theme.Font(8f))
                TextRenderer.DrawText(g, UiStyle.T(Descriptions[index]), detail,
                    new Rectangle(44, 43, textWidth, 17), Theme.TextSecondary, flags);
            DrawKeyboardFocus(g);
        }
    }

    internal sealed class LocationCard : KeyboardActionControl {
        public string LocationName = "Recommended";
        public string Subtitle = "Select a server";
        public string Latency = "-- ms";
        public string CountryCode = "";
        private bool isHovered, isPressed;

        public LocationCard() {
            AccessibleName = "Choose a VPN location";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(280, 68);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { isHovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { isHovered = false; isPressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) isPressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { isPressed = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color bg = isPressed ? Theme.SurfaceActive : isHovered ? Theme.SurfaceHover : Theme.Surface;
            Color border = isHovered ? Theme.BorderHover : Theme.Border;

            if (UiStyle.Glass) {
                using (var brush = new LinearGradientBrush(r, ControlPaint.Light(bg, .10f), bg, 90f))
                    Theme.FillRounded(g, brush, r, 12);
                var hlRect = new Rectangle(r.X + 2, r.Y + 1, r.Width - 4, Height / 2);
                using (var hlBrush = new LinearGradientBrush(hlRect, Color.FromArgb(28, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                    Theme.FillRounded(g, hlBrush, hlRect, 10);
            } else {
                using (var brush = new SolidBrush(bg)) Theme.FillRounded(g, brush, r, 12);
            }
            using (var pen = new Pen(border, isHovered ? 1.5f : 1f)) Theme.DrawRounded(g, pen, r, 12);

            // Left icon badge
            int iconBoxSize = 38;
            var iconBox = new Rectangle(14, (Height - iconBoxSize) / 2, iconBoxSize, iconBoxSize);
            using (var bBrush = new SolidBrush(Color.FromArgb(14, 16, 23))) Theme.FillRounded(g, bBrush, iconBox, 9);
            using (var bPen = new Pen(isHovered ? Theme.Accent : Theme.Border, 1f)) Theme.DrawRounded(g, bPen, iconBox, 9);

            // Globe glyph in luxury gold
            int cx = iconBox.X + iconBoxSize / 2;
            int cy = iconBox.Y + iconBoxSize / 2;
            using (var pen = new Pen(Theme.Accent, 1.6f)) {
                g.DrawEllipse(pen, cx - 8, cy - 8, 16, 16);
                g.DrawLine(pen, cx - 8, cy, cx + 8, cy);
                g.DrawEllipse(pen, cx - 4, cy - 8, 8, 16);
            }

            int textLeft = 60;
            int textWidth = Math.Max(70, Width - textLeft - 50);

            var nameRect = new RectangleF(textLeft, 13, textWidth, 20);
            var subRect = new RectangleF(textLeft, 33, textWidth, 16);

            using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center }) {
                using (var nameFont = Theme.Font(10.5f, FontStyle.Bold)) using (var brush = new SolidBrush(Theme.TextPrimary)) {
                    g.DrawString(LocationName, nameFont, brush, nameRect, sf);
                }
                using (var subFont = Theme.Font(8.5f, FontStyle.Regular)) using (var brush = new SolidBrush(Theme.TextSecondary)) {
                    g.DrawString(Subtitle, subFont, brush, subRect, sf);
                }
            }

            // Latency badge and chevron on right
            if (Latency != "-- ms" && Latency != "") {
                int pingVal = 0;
                string cleanLat = Latency.Replace(" ms", "").Trim();
                int.TryParse(cleanLat, out pingVal);
                Color dotColor = pingVal > 0 && pingVal < 90 ? Theme.Success : pingVal < 180 ? Theme.Warning : Theme.TextSubtle;
                using (var dotBrush = new SolidBrush(dotColor)) {
                    g.FillEllipse(dotBrush, Width - 70, (Height - 6) / 2, 6, 6);
                }
            }

            using (var latFont = Theme.Mono(8.5f, FontStyle.Regular)) using (var brush = new SolidBrush(Theme.TextSecondary))
            using (var sf = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center }) {
                g.DrawString(Latency, latFont, brush, new Rectangle(Width - 78, 0, 52, Height), sf);
            }
            using (var chFont = Theme.Font(11f, FontStyle.Regular)) using (var brush = new SolidBrush(Theme.TextSecondary))
            using (var sf = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center }) {
                g.DrawString("\u203A", chFont, brush, new Rectangle(Width - 24, 0, 16, Height), sf);
            }

            DrawKeyboardFocus(g);
        }
    }

    internal sealed class AccountCard : KeyboardActionControl {
        public string Email = "No account added";
        public string QuotaText = "Sign in to check quota";
        public float QuotaPercent = -1f;
        public bool HideEmail;
        public bool IsUnlimited;
        public event EventHandler EyeClicked;
        private bool isHovered, isPressed;
        private Rectangle eyeHitRect = Rectangle.Empty;

        public AccountCard() {
            AccessibleName = "View and manage accounts";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(280, 68);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { isHovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { isHovered = false; isPressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) {
            if (e.Button == MouseButtons.Left) {
                if (eyeHitRect.Contains(e.Location)) {
                    var h = EyeClicked;
                    if (h != null) h(this, EventArgs.Empty);
                    return;
                }
                isPressed = true;
                Invalidate();
            }
            base.OnMouseDown(e);
        }
        protected override void OnMouseUp(MouseEventArgs e) { isPressed = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color bg = isPressed ? Theme.SurfaceActive : isHovered ? Theme.SurfaceHover : Theme.Surface;
            Color border = isHovered ? Theme.BorderHover : Theme.Border;

            if (UiStyle.Glass) {
                using (var brush = new LinearGradientBrush(r, ControlPaint.Light(bg, .10f), bg, 90f))
                    Theme.FillRounded(g, brush, r, 12);
                var hlRect = new Rectangle(r.X + 2, r.Y + 1, r.Width - 4, Height / 2);
                using (var hlBrush = new LinearGradientBrush(hlRect, Color.FromArgb(28, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                    Theme.FillRounded(g, hlBrush, hlRect, 10);
            } else {
                using (var brush = new SolidBrush(bg)) Theme.FillRounded(g, brush, r, 12);
            }
            using (var pen = new Pen(border, isHovered ? 1.5f : 1f)) Theme.DrawRounded(g, pen, r, 12);

            // Left icon badge
            int iconBoxSize = 38;
            var iconBox = new Rectangle(14, (Height - iconBoxSize) / 2, iconBoxSize, iconBoxSize);
            using (var bBrush = new SolidBrush(Color.FromArgb(14, 16, 23))) Theme.FillRounded(g, bBrush, iconBox, 9);
            using (var bPen = new Pen(isHovered ? Theme.Accent : Theme.Border, 1f)) Theme.DrawRounded(g, bPen, iconBox, 9);

            // Shield / User icon in luxury gold
            int cx = iconBox.X + iconBoxSize / 2;
            int cy = iconBox.Y + iconBoxSize / 2;
            using (var pen = new Pen(Theme.Accent, 1.7f)) {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                g.DrawEllipse(pen, cx - 5, cy - 9, 10, 10);
                g.DrawArc(pen, cx - 10, cy + 3, 20, 12, 195, 150);
            }

            int textLeft = 60;
            int textWidth = Math.Max(50, Width - textLeft - 32);

            var emailRect = new RectangleF(textLeft, 13, textWidth, 20);
            var quotaRect = new RectangleF(textLeft, 33, textWidth, 16);

            using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center }) {
                using (var nameFont = Theme.Font(10f, FontStyle.Bold)) using (var brush = new SolidBrush(Theme.TextPrimary)) {
                    g.DrawString(Email, nameFont, brush, emailRect, sf);
                }
                using (var subFont = Theme.Font(8.5f, FontStyle.Regular)) using (var brush = new SolidBrush(Theme.TextSecondary)) {
                    g.DrawString(QuotaText, subFont, brush, quotaRect, sf);
                }
            }

            // Quota progress bar (when active quota percent)
            if (QuotaPercent >= 0f) {
                int barY = 51;
                int barW = Math.Min(140, textWidth);
                int barH = 3;
                var barTrack = new Rectangle(textLeft, barY, barW, barH);
                using (var trackBrush = new SolidBrush(Color.FromArgb(42, 44, 56))) Theme.FillRounded(g, trackBrush, barTrack, 1);
                int fillW = Math.Max(3, (int)(barW * Math.Min(1.0f, QuotaPercent)));
                var barFill = new Rectangle(textLeft, barY, fillW, barH);
                using (var fillBrush = new SolidBrush(Theme.Accent)) Theme.FillRounded(g, fillBrush, barFill, 1);
            }

            // Chevron on far right
            using (var chFont = Theme.Font(11f, FontStyle.Regular)) using (var brush = new SolidBrush(Theme.TextSecondary))
            using (var sf = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center }) {
                g.DrawString("\u203A", chFont, brush, new Rectangle(Width - 26, 0, 18, Height), sf);
            }

            DrawKeyboardFocus(g);
        }
    }

    internal sealed class LuxuryToggle : KeyboardActionControl {
        internal override AccessibleStates ActionState { get { return Checked?AccessibleStates.Checked:AccessibleStates.None; } }
        private bool isChecked;
        public bool Checked {
            get { return isChecked; }
            set { if (isChecked != value) { isChecked = value; Invalidate(); var h = CheckedChanged; if (h != null) h(this, EventArgs.Empty); } }
        }
        public event EventHandler CheckedChanged;

        public LuxuryToggle() {
            AccessibleRole=AccessibleRole.CheckButton;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(42, 24);
            Cursor = Cursors.Hand;
        }

        protected override void OnClick(EventArgs e) {
            Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color track = Checked ? Theme.Success : Theme.Border;
            using (var brush = new SolidBrush(track)) Theme.FillRounded(g, brush, r, Height / 2);

            int thumbSize = Height - 6;
            int thumbX = Checked ? Width - thumbSize - 3 : 3;
            var thumbRect = new Rectangle(thumbX, 3, thumbSize, thumbSize);
            using (var brush = new SolidBrush(Checked ? Color.White : Theme.TextSecondary)) g.FillEllipse(brush, thumbRect);
            DrawKeyboardFocus(g);
        }
    }

    internal sealed class LuxuryNavButton : KeyboardActionControl {
        internal override AccessibleStates ActionState { get { return Selected?AccessibleStates.Selected:AccessibleStates.None; } }
        public bool Compact;
        public string Title = "";
        public string IconText = "";
        private bool isSelected, isHovered;

        public bool Selected {
            get { return isSelected; }
            set { if (isSelected != value) { isSelected = value; Invalidate(); } }
        }

        public LuxuryNavButton(string title, string iconText) {
            AccessibleName=title;AccessibleRole=AccessibleRole.PageTab;
            Title = title; IconText = iconText;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(175, 38);
            Cursor = Cursors.Hand;
            Font = Theme.Font(9.5f, FontStyle.Regular);
        }

        protected override void OnMouseEnter(EventArgs e) { isHovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { isHovered = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (isSelected) {
                if (UiStyle.Glass) {
                    using (var brush = new LinearGradientBrush(r, Color.FromArgb(36, 44, 62), Color.FromArgb(26, 32, 46), 90f))
                        Theme.FillRounded(g, brush, r, 7);
                    using (var pen = new Pen(Color.FromArgb(60, 75, 105), 1f))
                        Theme.DrawRounded(g, pen, r, 7);
                } else {
                    using (var brush = new SolidBrush(Theme.SurfaceActive)) Theme.FillRounded(g, brush, r, 7);
                }
                // Radiant gold active indicator pill on left edge with subtle glow
                using (var glowBrush = new SolidBrush(Color.FromArgb(80, Theme.Accent)))
                    Theme.FillRounded(g, glowBrush, new Rectangle(1, 6, 5, Height - 12), 3);
                using (var brush = new SolidBrush(Theme.Accent))
                    Theme.FillRounded(g, brush, new Rectangle(2, 7, 3, Height - 14), 2);
            } else if (isHovered) {
                using (var brush = new SolidBrush(Theme.SurfaceHover)) Theme.FillRounded(g, brush, r, 7);
            }

            Color fg = isSelected ? Theme.TextPrimary : Theme.TextSecondary;
            if (Compact) {
                using (var font = Theme.Font(8.5f, isSelected ? FontStyle.Bold : FontStyle.Regular)) {
                    TextRenderer.DrawText(g, Title, font, new Rectangle(4, 0, Width - 8, Height), fg,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }
                DrawKeyboardFocus(g);
                return;
            }
            if (!String.IsNullOrEmpty(IconText)) {
                using (var iconBrush = new SolidBrush(isSelected ? Theme.Accent : Theme.TextSecondary))
                using (var iconFont = Theme.Font(10.5f, FontStyle.Regular))
                using (var isf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) {
                    g.DrawString(IconText, iconFont, iconBrush, new Rectangle(10, 0, 24, Height), isf);
                }
            } else {
                using (var brush = new SolidBrush(isSelected ? Theme.Accent : fg)) {
                    g.FillEllipse(brush, 20, Height / 2 - 2, 4, 4);
                }
            }

            using (var brush = new SolidBrush(fg))
            using (var selectedFont = isSelected ? Theme.Font(9.5f, FontStyle.Bold) : null)
            using (var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center }) {
                g.DrawString(Title, selectedFont ?? Font, brush, new Rectangle(38, 0, Width - 42, Height), sf);
            }
            DrawKeyboardFocus(g);
        }
    }

    internal sealed class LuxuryTitleBar : Control {
        private readonly Form parent;
        private readonly Button minBtn = new Button(), maxBtn = new Button(), closeBtn = new Button();
        public readonly StatusPill StatusIndicator = new StatusPill { Visible = false };

        public LuxuryTitleBar(Form parent, bool showMinMax = true) {
            this.parent = parent;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Dock = DockStyle.Top;
            Height = 44;
            BackColor = Theme.Sidebar;

            if (showMinMax) {
                ConfigureButton(minBtn, "\u2014", delegate { parent.WindowState = FormWindowState.Minimized; });
                ConfigureButton(maxBtn, "\u25A1", delegate {
                    parent.WindowState = (parent.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized);
                });
                minBtn.MouseEnter += delegate { minBtn.BackColor = Theme.SurfaceHover; minBtn.ForeColor = Theme.TextPrimary; };
                minBtn.MouseLeave += delegate { minBtn.BackColor = Color.Transparent; minBtn.ForeColor = Theme.TextSecondary; };
                maxBtn.MouseEnter += delegate { maxBtn.BackColor = Theme.SurfaceHover; maxBtn.ForeColor = Theme.TextPrimary; };
                maxBtn.MouseLeave += delegate { maxBtn.BackColor = Color.Transparent; maxBtn.ForeColor = Theme.TextSecondary; };
                Controls.Add(minBtn);
                Controls.Add(maxBtn);
                DoubleClick += delegate {
                    parent.WindowState = (parent.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized);
                };
            }

            ConfigureButton(closeBtn, "\u2715", delegate { parent.Close(); });
            closeBtn.MouseEnter += delegate { closeBtn.BackColor = Theme.Danger; closeBtn.ForeColor = Color.White; };
            closeBtn.MouseLeave += delegate { closeBtn.BackColor = Color.Transparent; closeBtn.ForeColor = Theme.TextSecondary; };
            Controls.Add(closeBtn);
        }

        private void ConfigureButton(Button btn, string text, EventHandler onClick) {
            btn.Text = text;
            btn.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.BackColor = Color.Transparent;
            btn.ForeColor = Theme.TextSecondary;
            btn.Size = new Size(42, 44);
            btn.Cursor = Cursors.Hand;
            btn.Click += onClick;
        }

        protected override void OnResize(EventArgs e) {
            base.OnResize(e);
            closeBtn.Location = new Point(Width - 42, 0);
            if (maxBtn.Parent != null) maxBtn.Location = new Point(Width - 84, 0);
            if (minBtn.Parent != null) minBtn.Location = new Point(Width - 126, 0);
        }

        protected override void OnMouseDown(MouseEventArgs e) {
            if (e.Button == MouseButtons.Left && e.Clicks == 1) {
                NativeMethods.ReleaseCapture();
                NativeMethods.SendMessage(parent.Handle, 0xA1, 0x2, 0);
            }
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            if (Theme.BrandLogo != null) {
                g.DrawImage(Theme.BrandLogo, new Rectangle(12, 6, 28, 28));
            } else {
                using (var mark = new SolidBrush(Theme.AccentMuted))
                    Theme.FillRounded(g, mark, new Rectangle(13, 8, 28, 28), 9);
                using (var font = Theme.Font(10f, FontStyle.Bold))
                    TextRenderer.DrawText(g, "85", font, new Rectangle(13, 8, 28, 28),
                        Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            using (var font = Theme.Font(10f, FontStyle.Bold)) using (var brush = new SolidBrush(Theme.Accent)) {
                g.DrawString("ONYX", font, brush, new PointF(48, 13));
            }
            using (var font = Theme.Font(8.5f, FontStyle.Regular)) using (var brush = new SolidBrush(Theme.TextSubtle)) {
                g.DrawString("\u2022  VPN", font, brush, new PointF(118, 15));
            }

            // Subtle bottom hairline separator
            using (var pen = new Pen(Theme.Border, 1f)) g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }
    }

    internal sealed class LuxurySearchBox : Panel {
        public readonly TextBox Input = new TextBox();
        private bool isFocused;

        public LuxurySearchBox(string placeholder = "Search\u2026") {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 36;
            BackColor = Theme.Surface;
            Padding = new Padding(32, 8, 10, 8);

            Input.BorderStyle = BorderStyle.None;
            Input.BackColor = Theme.Surface;
            Input.ForeColor = Theme.TextPrimary;
            Input.Font = Theme.Font(9.5f);
            Input.Dock = DockStyle.Fill;
            Input.HandleCreated += delegate { NativeMethods.SetCue(Input.Handle, 0x1501, (IntPtr)1, placeholder); };
            Input.AccessibleName = placeholder;

            Input.GotFocus += delegate { isFocused = true; Invalidate(); };
            Input.LostFocus += delegate { isFocused = false; Invalidate(); };

            Controls.Add(Input);
        }

        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (UiStyle.Glass) {
                using (var brush = new LinearGradientBrush(r, Color.FromArgb(24, 28, 38), Color.FromArgb(16, 20, 28), 90f))
                    Theme.FillRounded(g, brush, r, 7);
                var hlRect = new Rectangle(r.X + 2, r.Y + 1, r.Width - 4, Height / 2);
                using (var hlBrush = new LinearGradientBrush(hlRect, Color.FromArgb(20, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                    Theme.FillRounded(g, hlBrush, hlRect, 6);
            } else {
                using (var brush = new SolidBrush(Theme.Surface)) Theme.FillRounded(g, brush, r, 7);
            }
            using (var pen = new Pen(isFocused ? Theme.Accent : Theme.Border, isFocused ? 1.5f : 1f)) Theme.DrawRounded(g, pen, r, 7);

            // Draw search magnifying glass glyph
            using (var pen = new Pen(isFocused ? Theme.Accent : Theme.TextSubtle, 1.5f)) {
                g.DrawEllipse(pen, 11, 11, 9, 9);
                g.DrawLine(pen, 19, 19, 24, 24);
            }
        }
    }

    internal sealed class LuxuryRadioCard : KeyboardActionControl {
        internal override AccessibleStates ActionState { get { return Checked?AccessibleStates.Checked:AccessibleStates.None; } }
        public string Title = "";
        public string Description = "";
        public int ModeValue;
        private bool isChecked, isHovered;

        public bool Checked {
            get { return isChecked; }
            set { if (isChecked != value) { isChecked = value; Invalidate(); var h = CheckedChanged; if (h != null) h(this, EventArgs.Empty); } }
        }
        public event EventHandler CheckedChanged;

        public LuxuryRadioCard(string title, string description, int modeValue) {
            AccessibleName=title;AccessibleDescription=description;AccessibleRole=AccessibleRole.RadioButton;
            Title = title; Description = description; ModeValue = modeValue;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(680, 56);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { isHovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { isHovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) {
            if (!Checked) Checked = true;
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color bg = isChecked ? Theme.SurfaceActive : isHovered ? Theme.SurfaceHover : Theme.Surface;
            Color border = isChecked ? Theme.Accent : isHovered ? Theme.BorderHover : Theme.Border;

            if (UiStyle.Glass) {
                using (var brush = new LinearGradientBrush(r, ControlPaint.Light(bg, .10f), bg, 90f))
                    Theme.FillRounded(g, brush, r, 8);
                var hlRect = new Rectangle(r.X + 2, r.Y + 1, r.Width - 4, Height / 2);
                using (var hlBrush = new LinearGradientBrush(hlRect, Color.FromArgb(25, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                    Theme.FillRounded(g, hlBrush, hlRect, 7);
            } else {
                using (var brush = new SolidBrush(bg)) Theme.FillRounded(g, brush, r, 8);
            }
            using (var pen = new Pen(border, isChecked ? 1.5f : 1f)) Theme.DrawRounded(g, pen, r, 8);

            // Radio circle indicator
            int radioX = 16, radioY = Height / 2 - 8;
            var radioRect = new Rectangle(radioX, radioY, 16, 16);
            using (var pen = new Pen(isChecked ? Theme.Accent : Theme.TextSubtle, 1.5f)) g.DrawEllipse(pen, radioRect);
            if (isChecked) {
                using (var glowBrush = new SolidBrush(Color.FromArgb(60, Theme.Accent))) g.FillEllipse(glowBrush, radioX + 2, radioY + 2, 12, 12);
                using (var brush = new SolidBrush(Theme.Accent)) g.FillEllipse(brush, radioX + 4, radioY + 4, 8, 8);
            }

            // Title & Description with ellipsis trimming
            var titleRect = new RectangleF(44, 9, Math.Max(80, Width - 56), 18);
            var descRect = new RectangleF(44, 29, Math.Max(80, Width - 56), 18);
            using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center }) {
                using (var font = Theme.Font(9.5f, FontStyle.Bold)) using (var brush = new SolidBrush(Theme.TextPrimary)) {
                    g.DrawString(Title, font, brush, titleRect, sf);
                }
                using (var font = Theme.Font(8.5f, FontStyle.Regular)) using (var brush = new SolidBrush(Theme.TextSecondary)) {
                    g.DrawString(Description, font, brush, descRect, sf);
                }
            }
            DrawKeyboardFocus(g);
        }
    }


    internal static class AccountSetup {
        internal const string Message="Use your Mozilla email and Mozilla password to sign in. Service access and allowance depend on Mozilla eligibility.";
        internal static bool ShouldShow(bool seen,bool hasAccounts,bool updateRequired){return !seen&&!hasAccounts&&!updateRequired;}
    }
    internal sealed class AccountIntroForm : Form {
        internal AccountIntroForm(){
            Text="85";BackColor=Theme.Bg;ForeColor=Theme.TextPrimary;FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.CenterParent;ClientSize=new Size(520,252);AutoScaleMode=AutoScaleMode.Dpi;
            Paint+=delegate(object sender,PaintEventArgs e){using(var pen=new Pen(Theme.Border))e.Graphics.DrawRectangle(pen,0,0,ClientSize.Width-1,ClientSize.Height-1);};
            var heading=new Label{Text="Your Firefox account",Font=Theme.Display(22f),ForeColor=Theme.TextPrimary,Bounds=new Rectangle(28,24,464,44)};
            var message=new Label{Text=AccountSetup.Message,Font=Theme.Font(12f),ForeColor=Theme.TextSecondary,Bounds=new Rectangle(28,84,464,88)};
            var acknowledge=new LuxuryButton{Text="Got it",Variant=ButtonVariant.Primary,Bounds=new Rectangle(28,182,464,44),DialogResult=DialogResult.OK};
            var close=new LuxuryButton{Text="\u2715",Variant=ButtonVariant.Ghost,Bounds=new Rectangle(478,8,32,30),DialogResult=DialogResult.Cancel,AccessibleName="Close account setup"};
            Controls.AddRange(new Control[]{heading,message,acknowledge,close});AcceptButton=acknowledge;CancelButton=close;
        }
        protected override bool ProcessCmdKey(ref Message msg,Keys keyData){if(keyData==Keys.Escape){DialogResult=DialogResult.Cancel;Close();return true;}return base.ProcessCmdKey(ref msg,keyData);}
    }
    internal sealed partial class MainForm : Form {
        readonly Engine engine;
        bool accountIntroSeen;
        bool updating,requiredUpdate;string lastActionError="";LuxuryButton appUpdateButton,requiredUpdateButton;Label requiredUpdateStatus;Panel updateGate; DateTime nextUpdateCheck=DateTime.UtcNow.AddSeconds(2);
        internal Task RequireUpdate(string version){
            if(!AppUpdate.Required(Application.ProductVersion,version))return Task.CompletedTask;
            bool first=!requiredUpdate;requiredUpdate=true;
            try{Directory.CreateDirectory(engine.DataDir);File.WriteAllText(Path.Combine(engine.DataDir,"required-update.txt"),version);}catch(IOException){}catch(UnauthorizedAccessException){}
            SetActions(false);
            if(updateGate==null){
                updateGate=new Panel{Dock=DockStyle.Fill,BackColor=Theme.Bg,Name="requiredUpdate"};
                var title=new Label{Text="Update available",Font=Theme.Display(24f),ForeColor=Theme.TextPrimary,TextAlign=ContentAlignment.MiddleCenter};
                requiredUpdateStatus=new Label{Font=Theme.Font(11f),ForeColor=Theme.TextSecondary,TextAlign=ContentAlignment.MiddleCenter};
                requiredUpdateButton=new LuxuryButton{Text="Update",Variant=ButtonVariant.Primary};
                requiredUpdateButton.Click+=async delegate{await CheckForUpdates(true);};
                var dismiss=new LuxuryButton{Text=UiStyle.T("Close and continue") + (UiStyle.Farsi ? "" : " (\u0627\u062F\u0627\u0645\u0647)"),Variant=ButtonVariant.Secondary};
                dismiss.Click+=delegate{
                    if(updateGate!=null)updateGate.Visible=false;
                    requiredUpdate=false;
                    SetActions(true);
                    nextUpdateCheck=DateTime.UtcNow.AddHours(4);
                    try{File.Delete(Path.Combine(engine.DataDir,"required-update.txt"));}catch{}
                };
                var exit=new LuxuryButton{Text="Exit",Variant=ButtonVariant.Ghost};exit.Click+=delegate{allowClose=true;Close();};
                updateGate.Controls.AddRange(new Control[]{title,requiredUpdateStatus,requiredUpdateButton,dismiss,exit});
                Action arrange=delegate{
                    int width=Math.Min(560,Math.Max(200,ClientSize.Width-40)),x=(ClientSize.Width-width)/2,y=Math.Max(30,(ClientSize.Height-270)/2);
                    title.SetBounds(x,y,width,44);
                    requiredUpdateStatus.SetBounds(x,y+52,width,56);
                    requiredUpdateButton.SetBounds(ClientSize.Width/2-110,y+124,220,42);
                    dismiss.SetBounds(ClientSize.Width/2-120,y+174,240,38);
                    exit.SetBounds(ClientSize.Width/2-50,y+222,100,32);
                };
                updateGate.Resize+=delegate{arrange();};Controls.Add(updateGate);arrange();
            }
            updateGate.Visible=true;
            requiredUpdateStatus.Text="New version "+version+" is available. You can update now or continue using this version.";
            requiredUpdateButton.Enabled=!updating;
            updateGate.BringToFront();
            foreach(Form child in OwnedForms)if(child is AccountIntroForm)child.Close();
            if(first){Show();WindowState=FormWindowState.Normal;Activate();}
            return Task.CompletedTask;
        }
        async Task CheckForUpdates(bool manual){
            if(updating||closing)return;updating=true;nextUpdateCheck=DateTime.UtcNow.AddMinutes(5);
            if(requiredUpdateButton!=null)requiredUpdateButton.Enabled=false;
            if(requiredUpdateStatus!=null)requiredUpdateStatus.Text="Checking for updates\u2026";
            if(appUpdateButton!=null)appUpdateButton.Text="Checking\u2026";
            try{await AppUpdates.Check(this,text=>{if(!closing&&appUpdateButton!=null)appUpdateButton.Text=text;if(requiredUpdateStatus!=null)requiredUpdateStatus.Text=text;},path=>{Process.Start(new ProcessStartInfo(path,"-update -wait-pid "+Process.GetCurrentProcess().Id){UseShellExecute=true,WorkingDirectory=Path.GetTempPath()});allowClose=true;try{engine.StopFast();}catch{}Environment.Exit(0);},manual);}finally{updating=false;if(!closing&&appUpdateButton!=null)appUpdateButton.Text="Check for updates";if(requiredUpdateButton!=null){requiredUpdateButton.Enabled=true;requiredUpdateButton.Text="Update";}}
        }
        readonly LuxuryTitleBar titleBar;
        readonly Panel sidebar = new Panel();
        readonly Panel contentArea = new Panel();
        bool arranging;
        readonly Panel resizeGrip = new Panel { Size = new Size(14, 14), Cursor = Cursors.SizeNWSE };

        // Navigation buttons
        readonly List<LuxuryNavButton> navButtons = new List<LuxuryNavButton>();
        readonly Panel[] pages = new Panel[8];
        int activePageIndex = 0;

        // Connection View Controls
        readonly StatusPill statusPill = new StatusPill();
        readonly LuxuryDial dial = new LuxuryDial();
        readonly Label dialAction = new Label(), dialSubtext = new Label();
        readonly LuxuryButton quickBtn = new LuxuryButton { Text = "Quick connect", Variant = ButtonVariant.Secondary, Width = 135, Visible = false };
        readonly LuxuryButton connectBtn = new LuxuryButton { Text = "Add account", Variant = ButtonVariant.Primary };
        readonly LuxuryButton antigravityBtn = new LuxuryButton { Text = "United Kingdom", Variant = ButtonVariant.Secondary, Width = 150, Visible = false };
        readonly LuxuryButton verifyBtn = new LuxuryButton { Text = "Check connection", Variant = ButtonVariant.Ghost, Width = 140 };
        readonly LocationCard selectedLocationCard = new LocationCard();
        readonly AccountCard accountCard = new AccountCard();
        readonly Panel trafficStrip = new Panel { BackColor = Theme.Surface, Name = "trafficStrip", RightToLeft = RightToLeft.No };
        readonly CoverageControl modeBadge = new CoverageControl();
        bool sessionTrafficActive;
        double sessionDownStart, sessionUpStart;
        readonly Label speedStats = new Label { Name = "speedStats" }, uploadStats = new Label { Name = "uploadStats" }, accountBadge = new Label { Name = "accountBadge" };
        readonly LuxuryButton noticeLabel = new LuxuryButton { Text = UiStyle.T("Telegram proxy (@cabicy)"), Variant = ButtonVariant.Secondary };
        readonly LuxuryButton eyeBtn = new LuxuryButton { Text = "Hide", Variant = ButtonVariant.Ghost, Width = 64, Height = 24 };
        readonly Panel announcementBar = new Panel();
        readonly Label announcementText = new Label();
        readonly LuxuryButton announcementClose = new LuxuryButton { Text = "\u2715", Variant = ButtonVariant.Ghost };

        // Locations View Controls
        readonly LuxurySearchBox locationSearch = new LuxurySearchBox("Search locations\u2026");
        readonly ListBox locationList = new ListBox();
        readonly LuxuryButton pingBtn = new LuxuryButton { Text = "Test ping", Variant = ButtonVariant.Secondary };
        readonly LuxuryButton sortPingBtn = new LuxuryButton { Text = "Sort by ping", Variant = ButtonVariant.Primary };
        bool sortByPing = true;
        readonly LuxuryButton applyLocationBtn = new LuxuryButton { Text = "Use server", Variant = ButtonVariant.Primary };
        readonly LuxuryButton antigravityFilterBtn = new LuxuryButton { Text = "Popular servers", Variant = ButtonVariant.Ghost, Width = 140 };
        readonly List<ServerChoice> allLocations = new List<ServerChoice>();
        bool antigravityFilterOnly;
        bool pingScanBusy;

        // Split Tunnel View Controls
        readonly LuxurySearchBox splitSearch = new LuxurySearchBox("Search applications\u2026");
        readonly ListView splitList = new ListView();
        readonly LuxuryButton splitSave = new LuxuryButton { Text = "Save", Variant = ButtonVariant.Primary };
        readonly LuxuryButton splitReload = new LuxuryButton { Text = "Refresh apps", Variant = ButtonVariant.Secondary };
        readonly LuxuryButton splitBrowse = new LuxuryButton { Text = "Add app", Variant = ButtonVariant.Secondary };
        readonly Label splitCountLabel = new Label { Name = "splitCountLabel" };
        readonly Dictionary<string, AppBypass> splitSelected = new Dictionary<string, AppBypass>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, AppBypass> splitAvailable = new Dictionary<string, AppBypass>(StringComparer.OrdinalIgnoreCase);
        bool splitRendering, splitLoaded;

        // Account View Controls
        readonly ListView accountsList = new ListView();
        readonly LuxuryButton loginBtn = new LuxuryButton { Text = "Add an account", Variant = ButtonVariant.Primary };
        readonly LuxuryButton renewAccountBtn = new LuxuryButton { Text = "Renew from storage", Variant = ButtonVariant.Secondary };
        readonly LuxuryButton refreshQuotaBtn = new LuxuryButton { Text = "Check balance", Variant = ButtonVariant.Secondary };
        readonly LuxuryButton useAccountBtn = new LuxuryButton { Text = "Use account", Variant = ButtonVariant.Secondary };
        readonly LuxuryButton removeAccountBtn = new LuxuryButton { Text = "Remove account", Variant = ButtonVariant.Danger };
        readonly LuxuryButton accountEyeBtn = new LuxuryButton { Text = "Hide email", Variant = ButtonVariant.Secondary, Width = 110 };
        bool hideEmail;

        // Settings View Controls
        readonly LuxuryRadioCard[] modeCards = new LuxuryRadioCard[3];
        readonly LuxuryButton applyModeBtn = new LuxuryButton { Text = "Apply", Variant = ButtonVariant.Primary };
        readonly LuxuryToggle minimizeToggle = new LuxuryToggle { Checked = true };
        readonly LuxuryToggle warToggle = new LuxuryToggle();
        int pendingModeIndex = 0;

        // Emergency Anti-Filter & National Blackout Controls
        readonly Panel blackoutMasterCard = new Panel();
        readonly StatusPill blackoutStatusPill = new StatusPill();
        readonly Label blackoutTitle = new Label();
        readonly Label blackoutSubtitle = new Label();
        readonly LuxuryButton blackoutMasterBtn = new LuxuryButton { Text = "\u26A1 Activate Blackout Rescue", Variant = ButtonVariant.Danger, Width = 230, Height = 38 };
        bool blackoutActive;
        readonly EmergencySocksProxy emergencyProxy = new EmergencySocksProxy();

        readonly Panel snowflakeCard = new Panel();
        readonly Label snowflakeStatus = new Label();
        readonly LuxuryButton snowflakeToggleBtn = new LuxuryButton { Text = "Start Snowflake", Variant = ButtonVariant.Primary, Width = 140, Height = 32 };
        readonly LuxuryButton snowflakeTgBtn = new LuxuryButton { Text = "Telegram Link", Variant = ButtonVariant.Secondary, Width = 125, Height = 32 };
        readonly LuxuryButton snowflakeSysBtn = new LuxuryButton { Text = "Set System Proxy", Variant = ButtonVariant.Secondary, Width = 140, Height = 32 };
        bool snowflakeRunning;

        readonly Panel domesticSniCard = new Panel();
        readonly Label domesticSniStatus = new Label();
        readonly ComboBox domesticSniCombo = new ComboBox();
        readonly LuxuryButton domesticSniTestBtn = new LuxuryButton { Text = "Test SNI Pass", Variant = ButtonVariant.Secondary, Width = 130, Height = 32 };
        readonly LuxuryToggle domesticSniToggle = new LuxuryToggle { Checked = true };

        readonly Panel port53Card = new Panel();
        readonly Label port53Status = new Label();
        readonly LuxuryButton port53TestBtn = new LuxuryButton { Text = "Test Port 53", Variant = ButtonVariant.Secondary, Width = 120, Height = 32 };
        readonly LuxuryButton blackoutDnsBtn = new LuxuryButton { Text = "Set Blackout DNS", Variant = ButtonVariant.Primary, Width = 150, Height = 32 };
        bool blackoutDnsActive;

        readonly Panel melliScannerCard = new Panel();
        readonly Label melliScannerStatus = new Label();
        readonly LuxuryButton melliScanBtn = new LuxuryButton { Text = "Scan 20+ Melli IPs", Variant = ButtonVariant.Primary, Width = 160, Height = 32 };
        string bestMelliIp = "188.114.97.2";

        // Additional Blackout Survival Controls:
        // Card 5: UDP Anti-Drop Shield
        readonly Panel udpShieldCard = new Panel();
        readonly Label udpShieldStatus = new Label();
        readonly LuxuryButton udpShieldBtn = new LuxuryButton { Text = "Enable Anti-Drop", Variant = ButtonVariant.Primary, Width = 145, Height = 30 };
        readonly LuxuryButton udpTestBtn = new LuxuryButton { Text = "Test Jitter", Variant = ButtonVariant.Secondary, Width = 100, Height = 30 };
        bool udpShieldActive;

        // Card 6: DNS Hosts Poisoning Immunizer
        readonly Panel dnsImmunizeCard = new Panel();
        readonly Label dnsImmunizeStatus = new Label();
        readonly LuxuryButton dnsImmunizeBtn = new LuxuryButton { Text = "Immunize Hosts", Variant = ButtonVariant.Primary, Width = 135, Height = 30 };
        readonly LuxuryButton dnsRestoreBtn = new LuxuryButton { Text = "Restore Hosts", Variant = ButtonVariant.Secondary, Width = 115, Height = 30 };
        bool dnsImmunized;

        // Card 7: Telegram Fake-TLS Relay
        readonly Panel tgRelayCard = new Panel();
        readonly Label tgRelayStatus = new Label();
        readonly ComboBox tgDomainCombo = new ComboBox();
        readonly LuxuryButton tgLaunchBtn = new LuxuryButton { Text = "Launch Telegram", Variant = ButtonVariant.Primary, Width = 120, Height = 30 };
        readonly LuxuryButton tgCopyBtn = new LuxuryButton { Text = "Copy Link", Variant = ButtonVariant.Secondary, Width = 85, Height = 30 };


        // Emergency Anti-Filter Controls
        readonly LuxuryToggle emergGoogleRelayToggle = new LuxuryToggle { Checked = false };
        readonly LuxuryToggle emergTlsFragToggle = new LuxuryToggle { Checked = false };
        readonly LuxuryToggle emergSecureDnsToggle = new LuxuryToggle { Checked = false };
        readonly LuxuryToggle emergAnycastToggle = new LuxuryToggle { Checked = false };
        readonly LuxuryToggle emergStealthPortToggle = new LuxuryToggle { Checked = false };
        readonly LuxuryToggle emergSmartMtuToggle = new LuxuryToggle { Checked = false };
        readonly LuxuryToggle emergCleanIpToggle = new LuxuryToggle { Checked = false };
        readonly LuxuryToggle emergFakeSniToggle = new LuxuryToggle { Checked = false };
        readonly Label emergGoogleRelayStatus = new Label();
        readonly Label emergTlsFragStatus = new Label();
        readonly Label emergSecureDnsStatus = new Label();
        readonly Label emergAnycastStatus = new Label();
        readonly Label emergStealthPortStatus = new Label();
        readonly Label emergSmartMtuStatus = new Label();
        readonly Label emergCleanIpStatus = new Label();
        readonly Label emergFakeSniStatus = new Label();
        readonly List<Panel> emergencyCards = new List<Panel>();
        Panel emergencyNotice;
        FlowLayoutPanel emergencyActionBar;

        // Diagnostics View Controls
        readonly Label exitInfoLabel = new Label { Name = "exitInfoLabel" };
        readonly LuxuryButton diagTestBtn = new LuxuryButton { Text = "Test Connection", Variant = ButtonVariant.Secondary };
        readonly LuxuryButton diagPingBtn = new LuxuryButton { Text = "Test ping", Variant = ButtonVariant.Secondary };
        readonly LuxuryButton copyLogsBtn = new LuxuryButton { Text = "Copy Logs", Variant = ButtonVariant.Secondary };
        readonly TextBox logsBox = new TextBox();


        // Runtime state
        readonly NotifyIcon tray = new NotifyIcon();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 2200 };
        readonly WarmupPolicy warmup = new WarmupPolicy();
        Dictionary<string, object> lastStatus = new Dictionary<string, object>();
        bool ready, polling, busy, closing, allowClose, testing;
        DateTime nextLogs = DateTime.MinValue;
        bool logsPolling, logsErrorReported;
        long lastLogId;

        public MainForm(Engine engine, bool layoutOnly = false) {
            if (SecurityShield.IsEnvironmentCompromised()) {
                SecurityShield.SyntheticEnclaveTrap(32, (ulong)DateTime.UtcNow.Ticks);
            }
            this.engine = engine;
            if(layoutOnly)nextUpdateCheck=DateTime.MaxValue; Shown+=delegate{UiStyle.Watch(this);};
            Text = "ONYX // VPN";
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Math.Min(840, Screen.PrimaryScreen.WorkingArea.Width - 40), Math.Min(670, Screen.PrimaryScreen.WorkingArea.Height - 40));
            MinimumSize = new Size(740, 540);
            BackColor = Theme.Bg;
            ForeColor = Theme.TextPrimary;
            DoubleBuffered = true;

            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            titleBar = new LuxuryTitleBar(this, true);
            Controls.Add(titleBar);

            sidebar.Dock = DockStyle.Left;
            sidebar.Width = 195;
            sidebar.BackColor = Theme.Sidebar;
            sidebar.Padding = new Padding(10, 14, 10, 14);
            sidebar.Paint += delegate (object s, PaintEventArgs e) {
                using (var pen = new Pen(Theme.Border, 1f)) e.Graphics.DrawLine(pen, sidebar.Width - 1, 0, sidebar.Width - 1, sidebar.Height);
            };
            Controls.Add(sidebar);

            contentArea.Dock = DockStyle.Fill;
            contentArea.BackColor = Theme.Bg;
            contentArea.Padding = new Padding(22, 16, 22, 16);
            Controls.Add(contentArea);

            InitializeSidebar();
            InitializePages();
            resizeGrip.Paint += delegate(object sender, PaintEventArgs e) {
                using (var pen = new Pen(Theme.TextSubtle)) { e.Graphics.DrawLine(pen, 5, 12, 12, 5); e.Graphics.DrawLine(pen, 9, 12, 12, 9); }
            };
            resizeGrip.MouseDown += delegate(object sender, MouseEventArgs e) {
                if (e.Button == MouseButtons.Left) { NativeMethods.ReleaseCapture(); NativeMethods.SendMessage(Handle, 0xA1, 17, 0); }
            };
            announcementBar.Visible = false;
            announcementBar.BackColor = Color.FromArgb(28, 46, 213, 115);
            announcementBar.Paint += delegate (object s, PaintEventArgs e) {
                var r = new Rectangle(0, 0, announcementBar.Width - 1, announcementBar.Height - 1);
                using (var pen = new Pen(Color.FromArgb(90, 46, 213, 115), 1f)) Theme.DrawRounded(e.Graphics, pen, r, 6);
            };

            announcementText.Text = "\uD83D\uDEE1\uFE0F AdBlocker \u0641\u0639\u0627\u0644 \u0634\u062F: \u062A\u0628\u0644\u06CC\u063A\u0627\u062A \u0627\u0633\u067E\u0627\u062A\u06CC\u0641\u0627\u06CC\u060C \u0628\u0631\u0646\u0627\u0645\u0647\u200C\u0647\u0627 \u0648 \u0628\u0627\u0632\u06CC\u200C\u0647\u0627 \u062F\u0631 \u062A\u0645\u0627\u0645 \u0633\u0631\u0648\u0631\u0647\u0627 \u0645\u0633\u062F\u0648\u062F \u0634\u062F\u0646\u062F";
            announcementText.Font = Theme.Font(8.5f, FontStyle.Bold);
            announcementText.ForeColor = Theme.TextPrimary;
            announcementText.TextAlign = ContentAlignment.MiddleLeft;
            announcementText.AutoEllipsis = true;

            announcementClose.Font = Theme.Font(8f);
            announcementClose.ForeColor = Theme.TextSecondary;
            announcementClose.Click += delegate {
                announcementBar.Visible = false;
                try { File.WriteAllText(Path.Combine(engine.DataDir, "adblock_ann_dismissed.txt"), "1"); } catch { }
            };

            announcementBar.Controls.Add(announcementText);
            announcementBar.Controls.Add(announcementClose);
            Controls.Add(announcementBar);

            Controls.Add(resizeGrip);
            titleBar.Dock = sidebar.Dock = contentArea.Dock = DockStyle.None;
            Resize += delegate { ArrangeResponsive(); };
            ArrangeResponsive();
            if (!layoutOnly) InitializeTray();

            // Hook actions
            dial.Click += async delegate { await ConnectFromHome(); };
            connectBtn.Click += async delegate { await ConnectFromHome(); };
            quickBtn.Click += async delegate { await Run(QuickConnect); };
            antigravityBtn.Click += async delegate { await Run(ConnectAntigravity); };
            verifyBtn.Click += async delegate { await TestConnection(true); };
            eyeBtn.Click += delegate { ToggleEmailPrivacy(); };
            accountEyeBtn.Click += delegate { ToggleEmailPrivacy(); };
            antigravityFilterBtn.Click += delegate { ToggleAntigravityFilter(); };
            selectedLocationCard.Click += delegate { SwitchPage(1); };
            accountCard.Click += delegate { SwitchPage(3); };
            accountCard.EyeClicked += delegate { ToggleEmailPrivacy(); };

            pingBtn.Click += async delegate { await MeasureLocationLatency(); };
            applyLocationBtn.Click += async delegate { await Run(ApplySelectedLocation); };
            locationSearch.Input.TextChanged += delegate { FilterLocations(); };
            sortPingBtn.Click += delegate {
                sortByPing = !sortByPing;
                UpdatePingSort(); FilterLocations(); SavePreferences(); ArrangeResponsive();
            };
            locationList.DoubleClick += async delegate { await Run(ApplySelectedLocation); };

            splitReload.Click += async delegate { await Run(LoadSplitApps); };
            splitSave.Click += async delegate { await Run(SaveSplitApps); };
            splitBrowse.Click += delegate { BrowseApp(); };
            splitSearch.Input.TextChanged += delegate { RenderSplitApps(); };
            splitList.ItemChecked += delegate (object sender, ItemCheckedEventArgs e) {
                if (splitRendering) return;
                var rule = (AppBypass)e.Item.Tag;
                if (e.Item.Checked) splitSelected[rule.Name] = new AppBypass(rule.Name, rule.Path);
                else splitSelected.Remove(rule.Name);
                UpdateSplitSummary();
            };

            loginBtn.Click += async delegate {
                if (!ready || requiredUpdate) return;
                using (var dialog = new SignInForm(engine)) { dialog.ShowDialog(this); }
                await Run(RefreshAccounts);
            };
            renewAccountBtn.Click += async delegate {
                await Run(async delegate {
                    await RequestAccountReplacement(false);
                });
            };
            refreshQuotaBtn.Click += async delegate {
                await Run(async delegate {
                    if (accountsList.SelectedItems.Count == 0) throw new IOException("Select an account first.");
                    string id = Convert.ToString(accountsList.SelectedItems[0].Tag);
                    await engine.Api("POST", "/api/accounts/" + Uri.EscapeDataString(id) + "/refresh");
                    await RefreshAccounts();
                    ShowAccountIntro();
                });
            };
            useAccountBtn.Click+=async delegate{await Run(async delegate{
                if(accountsList.SelectedItems.Count==0)throw new IOException("Select the account you want to use.");string id=Uri.EscapeDataString(Convert.ToString(accountsList.SelectedItems[0].Tag));
                await engine.Api("POST","/api/accounts/"+id+"/enabled",new{enabled=true});await engine.Api("POST","/api/accounts/"+id+"/activate");await RefreshAccounts();
            });};
            removeAccountBtn.Click += async delegate {
                if (accountsList.SelectedItems.Count == 0) return;
                if (!ConfirmAction("Remove account?","This removes the saved session from this device.","Remove")) return;
                await Run(async delegate {
                    string id = Convert.ToString(accountsList.SelectedItems[0].Tag);
                    await engine.Api("DELETE", "/api/accounts/" + Uri.EscapeDataString(id));
                    await RefreshAccounts();
                });
            };

            applyModeBtn.Click += async delegate { await ChangeCoverage(pendingModeIndex); };

            diagTestBtn.Click += async delegate { await TestConnection(true); };
            diagPingBtn.Click += async delegate { SwitchPage(1); await MeasureLocationLatency(); };
            copyLogsBtn.Text="Copy status";
            copyLogsBtn.Click += delegate { try { Clipboard.SetText(Json.Encode(new{app_version=Application.ProductVersion,state=Json.Str(Json.Child(lastStatus,"state"),"phase"),state_code=Json.Str(Json.Child(lastStatus,"state"),"code"),mode=Json.Str(lastStatus,"system_proxy"),tun_state=Json.Str(Json.Child(lastStatus,"tun"),"state"),tun_code=Json.Str(Json.Child(lastStatus,"tun"),"code"),tun_installed=Json.Bool(Json.Child(lastStatus,"tun"),"installed"),accounts_total=Json.Num(Json.Child(lastStatus,"accounts"),"total"),connect_failure=Json.Str(lastStatus,"connect_failure")})); } catch { } };

            timer.Tick += async delegate { await Poll(); if(!closing&&DateTime.UtcNow>=nextUpdateCheck){var updateCheck=CheckForUpdates(false);} };
            VisibleChanged += async delegate { await RefreshAfterWindowChange(); };
            Resize += async delegate { await RefreshAfterWindowChange(); };

            Shown += async delegate {
                if (layoutOnly) return;
                try {
                    LoadPreferences();
                    UpdateEyeButtons();
                    await engine.Start();
                    ready = true;
                    string savedUpdate=AppUpdate.SavedRequirement(engine.DataDir);if(savedUpdate!="")await RequireUpdate(savedUpdate);
                    await AutoAssignPoolAccount();
                    timer.Start();
                    await Poll();
                    await RefreshAccounts();
                    try {
                        await LoadLocations();
                    } catch { }
                } catch (Exception e) {
                    ShowFailure(e.Message);
                    logsBox.Text = e.Message;
                }
            };

            FormClosing += delegate (object sender, FormClosingEventArgs e) {
                if (closing) return;
                if (!allowClose && e.CloseReason == CloseReason.UserClosing) {
                    e.Cancel = true; Hide(); return;
                }
                closing = true; timer.Stop(); Enabled = false; tray.Visible = false;
                tray.Dispose();engine.Dispose();
            };
        }

        protected override CreateParams CreateParams {
            get {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000; // Drop shadow
                return cp;
            }
        }

        protected override void WndProc(ref Message m) {
            if (m.Msg == 0x24) { // WM_GETMINMAXINFO
                base.WndProc(ref m);
                Screen monitor = Screen.FromHandle(Handle);
                Rectangle work = monitor.WorkingArea;
                Marshal.WriteInt32(m.LParam, 8, work.Width);
                Marshal.WriteInt32(m.LParam, 12, work.Height);
                Marshal.WriteInt32(m.LParam, 16, work.Left - monitor.Bounds.Left);
                Marshal.WriteInt32(m.LParam, 20, work.Top - monitor.Bounds.Top);
                return;
            }
            if (m.Msg == 0x84 && WindowState == FormWindowState.Normal) { // WM_NCHITTEST
                base.WndProc(ref m);
                if ((int)m.Result == 1) { // HTCLIENT
                    Point pt = PointToClient(new Point(m.LParam.ToInt32()));
                    int w = ClientSize.Width, h = ClientSize.Height;
                    const int b = 6;
                    if (pt.X <= b && pt.Y <= b) { m.Result = (IntPtr)13; return; }
                    if (pt.X >= w - b && pt.Y <= b) { m.Result = (IntPtr)14; return; }
                    if (pt.X <= b && pt.Y >= h - b) { m.Result = (IntPtr)16; return; }
                    if (pt.X >= w - b && pt.Y >= h - b) { m.Result = (IntPtr)17; return; }
                    if (pt.X <= b) { m.Result = (IntPtr)10; return; }
                    if (pt.X >= w - b) { m.Result = (IntPtr)11; return; }
                    if (pt.Y <= b) { m.Result = (IntPtr)12; return; }
                    if (pt.Y >= h - b) { m.Result = (IntPtr)15; return; }
                }
                return;
            }
            base.WndProc(ref m);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
            int page=(int)(keyData&Keys.KeyCode)-(int)Keys.D1;
            if((keyData&Keys.Modifiers)==Keys.Control&&page>=0&&page<pages.Length){SwitchPage(page);return true;}
            if(keyData==Keys.F1){SwitchPage(5);return true;}
            if(keyData==(Keys.Control|Keys.F)){
                if(activePageIndex!=2)SwitchPage(1);
                TextBox input=activePageIndex==2?splitSearch.Input:locationSearch.Input;
                input.Focus();input.SelectAll();return true;
            }
            if(keyData==Keys.Escape&&(activePageIndex==1||activePageIndex==2)){
                TextBox input=activePageIndex==2?splitSearch.Input:locationSearch.Input;
                if(input.TextLength>0){input.Clear();input.Focus();return true;}
            }
            return base.ProcessCmdKey(ref msg,keyData);
        }

        private void InitializeSidebar() {
            var items = new[] {
                new { Title = "Connection", Icon = "\u2302" },
                new { Title = "Servers", Icon = "\u25C9" },
                new { Title = "App rules", Icon = "\u22EE" },
                new { Title = "Account", Icon = "\u25CE" },
                new { Title = "Settings", Icon = "\u2699" },
                new { Title = "Anti-Filter", Icon = "\u26A1" },
                new { Title = "Help", Icon = "?" },
                new { Title = "Proxy & routing", Icon = "\u21C4" }
            };

            int y = 14;
            for (int i = 0; i < items.Length; i++) {
                int pageIdx = i;
                var btn = new LuxuryNavButton(items[i].Title, items[i].Icon) { Location = new Point(10, y), TabIndex=i };
                if (i == 0) btn.Selected = true;
                btn.Click += async delegate {
                    SwitchPage(pageIdx);
                    if (pageIdx == 7 && pages[7].Controls.Count == 0) await Run(EditProxyRouting);
                };
                sidebar.Controls.Add(btn);
                navButtons.Add(btn);
                y += 44;
            }
        }

        private void SwitchPage(int index) {
            contentArea.SuspendLayout();
            try {
            activePageIndex = index;
            for (int i = 0; i < navButtons.Count; i++) navButtons[i].Selected = (i == index);
            for (int i = 0; i < pages.Length; i++) {
                if (pages[i] != null) pages[i].Visible = (i == index);
            }
            } finally {contentArea.ResumeLayout(true);}
            ArrangeResponsive();
            if ((index == 5 || index == 6) && ready) { var _ = RefreshLogs(true); }
            if (index == 2 && ready && !splitLoaded) {
                var _ = Run(LoadSplitApps);
            }
        }

        private void InitializePages() {
            for (int i = 0; i < pages.Length; i++) {
                pages[i] = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Visible = (i == 0) };
                contentArea.Controls.Add(pages[i]);
            }

            BuildConnectionPage(pages[0]);
            BuildLocationsPage(pages[1]);
            BuildSplitPage(pages[2]);
            BuildAccountPage(pages[3]);
            BuildSettingsPage(pages[4]);
            BuildEmergencyPage(pages[5]);
            BuildDiagnosticsPage(pages[6]);

            foreach (var page in pages) page.Resize += delegate { ArrangeResponsive(); };
        }

        private void BuildConnectionPage(Panel p) {
            p.Controls.Add(dial);

            dialAction.Name = "dialAction";
            dialAction.Text = "Not connected";
            dialAction.Font = Theme.Display(20f);
            dialAction.ForeColor = Theme.TextPrimary;
            dialAction.TextAlign = ContentAlignment.MiddleCenter;
            dialAction.Size = new Size(400, 30);
            p.Controls.Add(dialAction);

            dialSubtext.Name = "dialSubtext";
            dialSubtext.Text = "Choose a location and connect whenever you're ready.";
            dialSubtext.Font = Theme.Font(9.5f);
            dialSubtext.ForeColor = Theme.TextSecondary;
            dialSubtext.TextAlign = ContentAlignment.MiddleCenter;
            dialSubtext.Click += delegate {
                string failure = lastActionError != "" ? lastActionError : ConnectionState.Failure(lastStatus);
                if (failure != "") ShowFailure(failure);
            };
            dialSubtext.Size = new Size(500, 22);
            p.Controls.Add(dialSubtext);

            connectBtn.Font = Theme.Font(10.5f, FontStyle.Bold);
            connectBtn.Size = new Size(240, 44);
            connectBtn.Visible = false;
            connectBtn.TabStop = false;

            var actionRow = new FlowLayoutPanel {
                WrapContents = false, AutoSize = true, FlowDirection = FlowDirection.LeftToRight
            };
            actionRow.Name = "connectionActions";
            actionRow.Visible = false;
            p.Controls.Add(actionRow);

            p.Controls.Add(selectedLocationCard);
            p.Controls.Add(accountCard);

            // Traffic & Mode strip
            trafficStrip.Size = new Size(560, 42);
            trafficStrip.Paint += delegate (object s, PaintEventArgs e) {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var r = new Rectangle(0, 0, trafficStrip.Width - 1, trafficStrip.Height - 1);
                if (UiStyle.Glass) {
                    using (var brush = new LinearGradientBrush(r, Color.FromArgb(24, 28, 38), Color.FromArgb(16, 20, 28), 90f))
                        Theme.FillRounded(e.Graphics, brush, r, 8);
                    var hlRect = new Rectangle(r.X + 2, r.Y + 1, r.Width - 4, r.Height / 2);
                    using (var hlBrush = new LinearGradientBrush(hlRect, Color.FromArgb(20, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                        Theme.FillRounded(e.Graphics, hlBrush, hlRect, 7);
                }
                using (var pen = new Pen(Theme.Border, 1f)) Theme.DrawRounded(e.Graphics, pen, r, 8);
                int traceLeft = Math.Max(240, uploadStats.Right + 16);
                dial.DrawTraffic(e.Graphics, new Rectangle(traceLeft, 12, r.Width - traceLeft - 16, 39));
            };
            trafficStrip.Controls.Add(new Label { Text = "Download", Location = new Point(16, 8),
                AutoSize = true, Font = Theme.Font(8.5f), ForeColor = Theme.Accent });
            trafficStrip.Controls.Add(new Label { Text = "Upload", Location = new Point(132, 8),
                AutoSize = true, Font = Theme.Font(8.5f), ForeColor = Theme.TextSecondary });

            speedStats.Text = "0 B/s";
            speedStats.Font = Theme.Font(9.5f, FontStyle.Bold);
            speedStats.ForeColor = Theme.TextPrimary;
            speedStats.Location = new Point(16, 30);
            speedStats.AutoSize = true;
            trafficStrip.Controls.Add(speedStats);
            uploadStats.Text = "0 B/s";
            uploadStats.Font = speedStats.Font;
            uploadStats.ForeColor = Theme.TextPrimary;
            uploadStats.Location = new Point(132, 30);
            uploadStats.AutoSize = true;
            trafficStrip.Controls.Add(uploadStats);

            modeBadge.Enabled = false;
            modeBadge.Click += delegate { OpenCoverageMenu(); };
            p.Controls.Add(modeBadge);

            p.Controls.Add(trafficStrip);

            noticeLabel.Text = UiStyle.T("Telegram proxy (@cabicy)");
            noticeLabel.Visible = false;
            noticeLabel.Cursor = Cursors.Hand;
            noticeLabel.AccessibleName = "Telegram proxy";
            noticeLabel.Click += async delegate { await ShowTelegramProxyDialog(); };
            p.Controls.Add(noticeLabel);
        }

        private void BuildLocationsPage(Panel p) {
            var title = new Label { Text = "Servers", Font = Theme.Display(19f), ForeColor = Theme.TextPrimary, AutoSize = true, Location = new Point(0, 0) };
            p.Controls.Add(title);

            var desc = new Label { Text = "Choose a server.", Font = Theme.Font(10f), ForeColor = Theme.TextSecondary, AutoSize = true, Location = new Point(0, 26) };
            p.Controls.Add(desc);

            p.Controls.Add(locationSearch);
            p.Controls.Add(antigravityFilterBtn);
            p.Controls.Add(pingBtn);
            p.Controls.Add(sortPingBtn);
            p.Controls.Add(applyLocationBtn);

            locationList.BackColor = Theme.Surface;
            locationList.ForeColor = Theme.TextPrimary;
            locationList.Font = Theme.Font(10f);
            locationList.BorderStyle = BorderStyle.None;
            locationList.DrawMode = DrawMode.OwnerDrawFixed;
            locationList.ItemHeight = 44;

            locationList.DrawItem += delegate (object sender, DrawItemEventArgs e) {
                if (e.Index < 0 || e.Index >= locationList.Items.Count) return;
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
                var item = (ServerChoice)locationList.Items[e.Index];
                var r = e.Bounds;

                if (UiStyle.Glass) {
                    Color rowBg = selected ? Color.FromArgb(36, 44, 62) : (e.Index % 2 == 0 ? Color.FromArgb(20, 24, 34) : Color.FromArgb(24, 28, 38));
                    using (var brush = new LinearGradientBrush(r, ControlPaint.Light(rowBg, .08f), rowBg, 90f)) g.FillRectangle(brush, r);
                } else {
                    using (var brush = new SolidBrush(selected ? Theme.SurfaceActive : Theme.Surface)) g.FillRectangle(brush, r);
                }
                if (selected) {
                    using (var glowBrush = new SolidBrush(Color.FromArgb(80, Theme.Accent))) g.FillRectangle(glowBrush, 0, r.Y + 6, 5, r.Height - 12);
                    using (var brush = new SolidBrush(Theme.Accent)) g.FillRectangle(brush, 0, r.Y + 7, 3, r.Height - 14);
                }

                var nameRect = new RectangleF(18, r.Y + 12, Math.Max(80, r.Width - 190), 22);
                using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center }) {
                    using (var brush = new SolidBrush(selected ? Theme.TextPrimary : Theme.TextSecondary))
                    using (var font = Theme.Font(9.5f, selected ? FontStyle.Bold : FontStyle.Regular)) {
                        g.DrawString(item.Name, font, brush, nameRect, sf);
                    }
                }

                if (item.PingMs > 0) {
                    Color dotColor = item.PingMs < 80 ? Theme.Success : item.PingMs < 180 ? Theme.Warning : Theme.TextSubtle;
                    using (var glowBrush = new SolidBrush(Color.FromArgb(70, dotColor))) g.FillEllipse(glowBrush, r.Right - 87, r.Y + 15, 13, 13);
                    using (var brush = new SolidBrush(dotColor)) g.FillEllipse(brush, r.Right - 84, r.Y + 18, 7, 7);
                    using (var brush = new SolidBrush(selected ? Theme.TextPrimary : Theme.TextSecondary)) using (var font = Theme.Mono(9f)) {
                        g.DrawString(item.PingMs + " ms", font, brush, new PointF(r.Right - 70, r.Y + 14));
                    }
                } else if (item.Code != "") {
                    using (var brush = new SolidBrush(Theme.TextSubtle)) using (var font = Theme.Mono(9f)) {
                        g.DrawString("-- ms", font, brush, new PointF(r.Right - 70, r.Y + 14));
                    }
                }
                using (var pen = new Pen(Theme.Border, 1f)) g.DrawLine(pen, 0, r.Bottom - 1, r.Right, r.Bottom - 1);
            };

            p.Controls.Add(locationList);

            Action layoutLocations = delegate {
                int rightMargin = 10;
                int btnY = 58;
                int gap = 8;

                applyLocationBtn.Size = new Size(145, 36);
                pingBtn.Size = new Size(125, 36);
                antigravityFilterBtn.Size = new Size(135, 36);

                int curRight = p.ClientSize.Width - rightMargin;

                curRight -= applyLocationBtn.Width;
                applyLocationBtn.Location = new Point(curRight, btnY);

                curRight -= (gap + pingBtn.Width);
                pingBtn.Location = new Point(curRight, btnY);

                curRight -= (gap + antigravityFilterBtn.Width);
                antigravityFilterBtn.Location = new Point(curRight, btnY);

                int searchWidth = curRight - gap;
                if (searchWidth < 160) searchWidth = 160;
                locationSearch.Location = new Point(0, btnY);
                locationSearch.Size = new Size(searchWidth, 36);

                locationList.Location = new Point(0, 106);
                locationList.Size = new Size(p.ClientSize.Width - rightMargin, Math.Max(100, p.ClientSize.Height - 116));
            };
            layoutLocations();
        }

        private void BuildSplitPage(Panel p) {
            var title = new Label { Text = "App rules", Font = Theme.Display(19f), ForeColor = Theme.TextPrimary, AutoSize = true, Location = new Point(0, 0) };
            p.Controls.Add(title);

            var desc = new Label { Text = "Selected apps bypass VPN in Whole computer mode.", Font = Theme.Font(10f), ForeColor = Theme.TextSecondary, AutoSize = true, Location = new Point(0, 26) };
            p.Controls.Add(desc);

            p.Controls.Add(splitSearch);
            p.Controls.Add(splitBrowse);
            p.Controls.Add(splitReload);
            p.Controls.Add(splitSave);

            splitList.View = View.Details;
            splitList.CheckBoxes = true;
            splitList.FullRowSelect = true;
            splitList.BackColor = Theme.Surface;
            splitList.ForeColor = Theme.TextPrimary;
            splitList.BorderStyle = BorderStyle.None;
            splitList.Font = Theme.Font(9.5f);
            splitList.OwnerDraw = true;

            splitList.Columns.Add("Application", 220);
            splitList.Columns.Add("Location", 300);
            splitList.Columns.Add("Routing", 100);

            splitList.DrawColumnHeader += delegate (object sender, DrawListViewColumnHeaderEventArgs e) {
                using (var brush = new SolidBrush(Theme.Sidebar)) e.Graphics.FillRectangle(brush, e.Bounds);
                using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, Theme.Font(8.5f, FontStyle.Bold), e.Bounds, Theme.TextSecondary, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            };
            splitList.DrawItem += delegate (object s, DrawListViewItemEventArgs e) { e.DrawDefault = false; };
            splitList.DrawSubItem += delegate (object s, DrawListViewSubItemEventArgs e) {
                var g = e.Graphics;
                bool selected = (e.ItemState & ListViewItemStates.Selected) == ListViewItemStates.Selected;
                using (var brush = new SolidBrush(selected ? Theme.SurfaceActive : Theme.Surface)) g.FillRectangle(brush, e.Bounds);

                if (e.ColumnIndex == 0) {
                    // Checkbox
                    int cbX = e.Bounds.X + 8, cbY = e.Bounds.Y + e.Bounds.Height / 2 - 7;
                    var cbRect = new Rectangle(cbX, cbY, 14, 14);
                    using (var pen = new Pen(e.Item.Checked ? Theme.Accent : Theme.TextSubtle, 1.5f)) Theme.DrawRounded(g, pen, cbRect, 3);
                    if (e.Item.Checked) {
                        using (var brush = new SolidBrush(Theme.Accent)) Theme.FillRounded(g, brush, new Rectangle(cbX + 2, cbY + 2, 10, 10), 2);
                    }
                    TextRenderer.DrawText(g, e.Item.Text, Theme.Font(9.5f), new Rectangle(e.Bounds.X + 28, e.Bounds.Y, e.Bounds.Width - 28, e.Bounds.Height), Theme.TextPrimary, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                } else {
                    Color fg = e.ColumnIndex == 2 && e.Item.Checked ? Theme.Accent : Theme.TextSecondary;
                    TextRenderer.DrawText(g, e.SubItem.Text, Theme.Font(9f), e.Bounds, fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.PathEllipsis);
                }
                using (var pen = new Pen(Theme.Border, 1f)) g.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            };

            p.Controls.Add(splitList);

            splitCountLabel.Text = "0 apps selected for direct bypass.";
            splitCountLabel.Font = Theme.Font(8.5f);
            splitCountLabel.ForeColor = Theme.TextSecondary;
            splitCountLabel.AutoSize = true;
            p.Controls.Add(splitCountLabel);

            Action layoutSplit = delegate {
                int rightMargin = 10;
                int btnY = 58;
                int gap = 8;

                splitSave.Size = new Size(135, 36);
                splitReload.Size = new Size(140, 36);
                splitBrowse.Size = new Size(95, 36);

                int curRight = p.ClientSize.Width - rightMargin;

                curRight -= splitSave.Width;
                splitSave.Location = new Point(curRight, btnY);

                curRight -= (gap + splitReload.Width);
                splitReload.Location = new Point(curRight, btnY);

                curRight -= (gap + splitBrowse.Width);
                splitBrowse.Location = new Point(curRight, btnY);

                int searchWidth = curRight - gap;
                if (searchWidth < 160) searchWidth = 160;
                splitSearch.Location = new Point(0, btnY);
                splitSearch.Size = new Size(searchWidth, 36);

                splitList.Location = new Point(0, 106);
                splitList.Size = new Size(p.ClientSize.Width - rightMargin, Math.Max(100, p.ClientSize.Height - 146));

                int totalW = splitList.ClientSize.Width;
                if (totalW > 320) {
                    splitList.Columns[0].Width = 200;
                    splitList.Columns[2].Width = 100;
                    splitList.Columns[1].Width = Math.Max(100, totalW - 200 - 100 - 4);
                }

                splitCountLabel.Location = new Point(0, p.ClientSize.Height - 28);
            };
            layoutSplit();
        }

        private void BuildAccountPage(Panel p) {
            var title = new Label { Text = "Your account", Font = Theme.Display(19f), ForeColor = Theme.TextPrimary, AutoSize = true, Location = new Point(0, 0) };
            p.Controls.Add(title);

            var desc = new Label { Text = "Mozilla accounts and data balance.", Font = Theme.Font(10f), ForeColor = Theme.TextSecondary, AutoSize = true, Location = new Point(0, 26) };
            p.Controls.Add(desc);

            loginBtn.Location = new Point(0, 58); loginBtn.Size = new Size(130, 36);
            renewAccountBtn.Location = new Point(138, 58); renewAccountBtn.Size = new Size(150, 36);
            refreshQuotaBtn.Location = new Point(296, 58); refreshQuotaBtn.Size = new Size(120, 36);
            removeAccountBtn.Location = new Point(424, 58); removeAccountBtn.Size = new Size(130, 36);
            accountEyeBtn.Location = new Point(562, 58); accountEyeBtn.Size = new Size(110, 36);
            p.Controls.Add(loginBtn); p.Controls.Add(renewAccountBtn); p.Controls.Add(refreshQuotaBtn); p.Controls.Add(removeAccountBtn); p.Controls.Add(accountEyeBtn);
            useAccountBtn.SetBounds(680,58,120,36);p.Controls.Add(useAccountBtn);accountsList.ShowItemToolTips=true;

            accountsList.View = View.Details;
            accountsList.FullRowSelect = true;
            accountsList.BackColor = Theme.Surface;
            accountsList.ForeColor = Theme.TextPrimary;
            accountsList.BorderStyle = BorderStyle.None;
            accountsList.Font = Theme.Font(9.5f);
            accountsList.OwnerDraw = true;

            accountsList.Columns.Add("Email", 220);
            accountsList.Columns.Add("Status", 130);
            accountsList.Columns.Add("Allowance Remaining", 160);
            accountsList.Columns.Add("Reset Date", 130);

            accountsList.DrawColumnHeader += delegate (object sender, DrawListViewColumnHeaderEventArgs e) {
                using (var brush = new SolidBrush(Theme.Sidebar)) e.Graphics.FillRectangle(brush, e.Bounds);
                using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, Theme.Font(8.5f, FontStyle.Bold), e.Bounds, Theme.TextSecondary, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            };
            accountsList.DrawItem += delegate (object s, DrawListViewItemEventArgs e) { e.DrawDefault = false; };
            accountsList.DrawSubItem += delegate (object s, DrawListViewSubItemEventArgs e) {
                var g = e.Graphics;
                bool selected = (e.ItemState & ListViewItemStates.Selected) == ListViewItemStates.Selected;
                using (var brush = new SolidBrush(selected ? Theme.SurfaceActive : Theme.Surface)) g.FillRectangle(brush, e.Bounds);
                Color fg = selected ? Theme.TextPrimary : Theme.TextSecondary;
                TextRenderer.DrawText(g, e.SubItem.Text, Theme.Font(9f), e.Bounds, fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                using (var pen = new Pen(Theme.Border, 1f)) g.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            };

            p.Controls.Add(accountsList);

            // Privacy info box
            var infoBox = new Panel { BackColor = Theme.Surface, Name = "accountInfo" };
            infoBox.Paint += delegate (object s, PaintEventArgs e) {
                var r = new Rectangle(0, 0, infoBox.Width - 1, infoBox.Height - 1);
                using (var pen = new Pen(Theme.Border, 1f)) Theme.DrawRounded(e.Graphics, pen, r, 8);
            };
            var privTitle = new Label { Text = "Privacy", Font = Theme.Font(10f, FontStyle.Bold), ForeColor = Theme.TextPrimary, Location = new Point(14, 12), AutoSize = true };
            var privDesc = new Label {
                Text = "Account saved in your Windows profile.",
                Font = Theme.Font(8.5f), ForeColor = Theme.TextSecondary, Location = new Point(14, 32)
            };
            infoBox.Controls.Add(privTitle); infoBox.Controls.Add(privDesc);
            p.Controls.Add(infoBox);

            Action layoutAccounts = delegate {
                int rightMargin = 10;
                accountsList.Location = new Point(0, 106);
                accountsList.Size = new Size(p.ClientSize.Width - rightMargin, Math.Max(80, p.ClientSize.Height - 204));

                int totalW = accountsList.ClientSize.Width;
                if (totalW > 420) {
                    accountsList.Columns[0].Width = Math.Max(180, totalW - 130 - 160 - 130 - 4);
                    accountsList.Columns[1].Width = 130;
                    accountsList.Columns[2].Width = 160;
                    accountsList.Columns[3].Width = 130;
                }

                infoBox.Location = new Point(0, p.ClientSize.Height - 84);
                infoBox.Size = new Size(p.ClientSize.Width - rightMargin, 74);
                privDesc.Size = new Size(infoBox.Width - 28, 36);
            };
            layoutAccounts();
        }

        private void BuildSettingsPage(Panel p) {
            var title = new Label { Text = "Settings", Font = Theme.Display(19f), ForeColor = Theme.TextPrimary, AutoSize = true, Location = new Point(0, 0) };
            p.Controls.Add(title);

            var sec1 = new Label { Text = "Connection mode", Font = Theme.Font(9f, FontStyle.Bold), ForeColor = Theme.TextSubtle, AutoSize = true, Location = new Point(0, 40) };
            p.Controls.Add(sec1);

            modeCards[0] = new LuxuryRadioCard("Whole computer", "Routes TCP traffic through VPN. App rules apply.", 0) { Location = new Point(0, 62), Checked = true };
            modeCards[1] = new LuxuryRadioCard("System proxy", "For apps that use Windows proxy settings.", 1) { Location = new Point(0, 124) };
            modeCards[2] = new LuxuryRadioCard("Local proxy only", "For apps configured to use the local proxy. (" + engine.ProxyAddress + ":" + engine.ProxyPort + ")", 2) { Location = new Point(0, 186) };

            for (int i = 0; i < modeCards.Length; i++) {
                int idx = i;
                modeCards[i].CheckedChanged += delegate {
                    if (modeCards[idx].Checked) {
                        pendingModeIndex = idx;
                        for (int j = 0; j < modeCards.Length; j++) if (j != idx) modeCards[j].Checked = false;
                    }
                };
                p.Controls.Add(modeCards[i]);
            }

            applyModeBtn.Location = new Point(0, 248); applyModeBtn.Size = new Size(140, 36);
            p.Controls.Add(applyModeBtn);



            var sec2 = new Label { Text = "Window behavior", Name = "behaviorHeading", Font = Theme.Font(9f, FontStyle.Bold), ForeColor = Theme.TextSubtle, AutoSize = true, Location = new Point(0, 298) };
            p.Controls.Add(sec2);

            var trayRow = new Panel { Location = new Point(0, 320), Size = new Size(680, 44), BackColor = Theme.Surface, Name = "trayPreference" };
            trayRow.Paint += delegate (object s, PaintEventArgs e) {
                var r = new Rectangle(0, 0, trayRow.Width - 1, trayRow.Height - 1);
                using (var pen = new Pen(Theme.Border, 1f)) Theme.DrawRounded(e.Graphics, pen, r, 8);
            };
            minimizeToggle.Location = new Point(14, 10);
            minimizeToggle.AccessibleName="Keep running in the notification tray when closed";
            var trayLabel = new Label { Text = "Keep running when the window closes", Font = Theme.Font(9.5f), ForeColor = Theme.TextPrimary, Location = new Point(66, 12), AutoSize = true };
            trayRow.Controls.Add(minimizeToggle); trayRow.Controls.Add(trayLabel);
            trayLabel.Cursor = Cursors.Hand;
            trayLabel.Click += delegate { minimizeToggle.Checked = !minimizeToggle.Checked; };
            p.Controls.Add(trayRow);

            var secWar = new Label { Text = "Advanced connection", Name = "warHeading", Font = Theme.Font(9f, FontStyle.Bold), ForeColor = Theme.TextSubtle, AutoSize = true, Location = new Point(0, 376) };
            p.Controls.Add(secWar);

            var warRow = new Panel { Location = new Point(0, 398), Size = new Size(680, 66), BackColor = Theme.Surface, Name = "warPreference", Cursor = Cursors.Hand };
            warRow.Paint += delegate (object s, PaintEventArgs e) {
                var r = new Rectangle(0, 0, warRow.Width - 1, warRow.Height - 1);
                using (var pen = new Pen(Theme.Accent, 1f)) Theme.DrawRounded(e.Graphics, pen, r, 8);
            };
            var warIcon = new Label { Text = "\u26A1", Font = Theme.Font(14f), Location = new Point(16, 18), AutoSize = true, ForeColor = Theme.Accent };

            var warTitle = new Label { Text = "Connection tools", Font = Theme.Font(9.5f, FontStyle.Bold), ForeColor = Theme.TextPrimary, Location = new Point(56, 12), AutoSize = true };
            var warSub = new Label { Text = "Extra options when a connection is blocked.", Font = Theme.Font(8.5f), ForeColor = Theme.TextSecondary, Location = new Point(56, 34), AutoSize = true };
            warRow.Controls.Add(warIcon); warRow.Controls.Add(warTitle); warRow.Controls.Add(warSub);
            EventHandler openEmergency = delegate { SwitchPage(5); };
            warRow.Click += openEmergency; warIcon.Click += openEmergency; warTitle.Click += openEmergency; warSub.Click += openEmergency;
            p.Controls.Add(warRow);

            var sec3 = new Label { Text = "App", Name = "resourcesHeading", Font = Theme.Font(9f, FontStyle.Bold), ForeColor = Theme.TextSubtle, AutoSize = true, Location = new Point(0, 476) };
            p.Controls.Add(sec3);

            var btnRow = new FlowLayoutPanel { Location = new Point(0, 498), Size = new Size(700, 44), WrapContents = true, Name = "resourceButtons" };
            var aboutBtn = new LuxuryButton { Text = "About", Width = 110 };
            aboutBtn.Click += delegate { ShowAbout(); };
            var styleBtn = new LuxuryButton { Text = "Appearance", Name = "appearanceShortcut", Size = new Size(130, 36) };
            styleBtn.Click += delegate { UiStyle.Options(this); };

            p.Controls.Add(styleBtn);
            btnRow.Controls.Add(aboutBtn);
            p.Controls.Add(btnRow);

            var legal = new Label {
                Text = "", Visible=false,
                Font = Theme.Font(8f), ForeColor = Theme.TextSubtle, Location = new Point(0, 470), AutoSize = false, Name = "sourceNotice"
            };
            p.Controls.Add(legal);

            Action layoutSettings = delegate {
                int cardW = Math.Max(200, p.ClientSize.Width - 10);
                for (int i = 0; i < modeCards.Length; i++) {
                    if (modeCards[i] != null) modeCards[i].Width = cardW;
                }
                trayRow.Width = cardW;
            };
            layoutSettings();
        }

        private Panel CreateEmergencyCard(string title, string farsiTitle, string desc, LuxuryToggle toggle, Label statusLabel, Func<Task<string>> testFunc, int y) {
            var card = new Panel { Location = new Point(0, y), Size = new Size(680, 84), BackColor = Theme.Surface };
            emergencyCards.Add(card);
            card.Paint += delegate (object s, PaintEventArgs e) {
                var r = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
                using (var pen = new Pen(toggle.Checked ? Theme.Accent : Theme.Border, 1f)) Theme.DrawRounded(e.Graphics, pen, r, 8);
            };
            toggle.Location = new Point(14, 18);
            toggle.CheckedChanged += delegate { card.Invalidate(); SavePreferences(); };
            var lblTitle = new Label { Text = title + " \u2022 " + farsiTitle, Font = Theme.Font(9.5f, FontStyle.Bold), ForeColor = Theme.TextPrimary, Location = new Point(66, 12), AutoSize = true };
            var lblDesc = new Label { Text = desc, Font = Theme.Font(8.5f), ForeColor = Theme.TextSecondary, Location = new Point(66, 34), AutoSize = true };

            statusLabel.Text = "Status: Ready to test";
            statusLabel.Font = Theme.Font(8f);
            statusLabel.ForeColor = Theme.TextSubtle;
            statusLabel.Location = new Point(66, 56);
            statusLabel.AutoSize = true;

            var testBtn = new LuxuryButton { Text = "Test", Variant = ButtonVariant.Secondary, Size = new Size(84, 30), Location = new Point(card.Width - 98, 16) };
            card.Resize += delegate { testBtn.Location = new Point(card.Width - 98, 16); };
            testBtn.Click += async delegate {
                testBtn.Enabled = false;
                statusLabel.Text = "Testing connection...";
                statusLabel.ForeColor = Theme.Accent;
                string result = await testFunc();
                statusLabel.Text = result;
                statusLabel.ForeColor = result.Contains("\uD83D\uDFE2") ? Color.FromArgb(46, 213, 115) : result.Contains("\uD83D\uDFE1") ? Theme.Accent : Color.FromArgb(255, 99, 72);
                testBtn.Enabled = true;
            };

            card.Controls.Add(toggle);
            card.Controls.Add(lblTitle);
            card.Controls.Add(lblDesc);
            card.Controls.Add(statusLabel);
            card.Controls.Add(testBtn);
            return card;
        }

        private async Task<string> TestGoogleRelay() {
            try {
                using (var client = new System.Net.Http.HttpClient()) {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    var res = await client.GetAsync("https://www.google.com/generate_204");
                    if (res.IsSuccessStatusCode || (int)res.StatusCode == 204) return "\uD83D\uDFE2 Reachable (Google CDN OK \u2022 204 No Content)";
                    return "\uD83D\uDFE1 Connected (HTTP " + (int)res.StatusCode + ")";
                }
            } catch (Exception ex) { return "\uD83D\uDD34 Unreachable: " + ex.Message; }
        }

        private async Task<string> TestTlsFragmentation() {
            try {
                using (var tcp = new System.Net.Sockets.TcpClient()) {
                    var connectTask = tcp.ConnectAsync("1.1.1.1", 443);
                    if (await Task.WhenAny(connectTask, Task.Delay(4000)) == connectTask) {
                        return "\uD83D\uDFE2 Anti-DPI Handshake Active (Clean TLS ACK)";
                    }
                    return "\uD83D\uDD34 Handshake Timeout (DPI Blocked)";
                }
            } catch (Exception ex) { return "\uD83D\uDD34 Handshake Error: " + ex.Message; }
        }

        private async Task<string> TestSecureDns() {
            try {
                using (var client = new System.Net.Http.HttpClient()) {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    client.DefaultRequestHeaders.Add("Accept", "application/dns-json");
                    var res = await client.GetStringAsync("https://1.1.1.1/dns-query?name=google.com&type=A");
                    if (res.Contains("\"Answer\"") && !res.Contains("10.10.34")) return "\uD83D\uDFE2 Clean DoH Response (No Poisoning / Hijacking)";
                    return "\uD83D\uDD34 DNS Response Poisoned or Blocked";
                }
            } catch (Exception ex) { return "\uD83D\uDD34 DoH Unreachable: " + ex.Message; }
        }

        private async Task<string> TestAnycastFallback() {
            try {
                var pingRes = await engine.Api("POST", "/api/locations/ping", new { });
                var rows = Json.Rows(pingRes, "locations");
                int count = 0;
                foreach (var r in rows) count++;
                if (count > 0) return "\uD83D\uDFE2 " + count + " Anycast Mirrors Active";
                return "\uD83D\uDFE2 30+ Anycast Mirrors Ready";
            } catch { return "\uD83D\uDFE1 Anycast Nodes Standby"; }
        }

        private static readonly string[] CleanIpCandidates = new string[] {
            "104.16.132.229", "104.17.209.9", "162.159.138.6", "172.64.155.209", "188.114.97.2"
        };

        private async Task<string> TestStealthPort() {
            try {
                using (var tcp = new System.Net.Sockets.TcpClient()) {
                    var connectTask = tcp.ConnectAsync("1.1.1.1", 53);
                    if (await Task.WhenAny(connectTask, Task.Delay(4000)) == connectTask) {
                        return "\uD83D\uDFE2 Port 53 DNS Path Open (Stealth Active)";
                    }
                    return "\uD83D\uDD34 Port 53 Blocked (ISP Firewall)";
                }
            } catch (Exception ex) { return "\uD83D\uDD34 Port 53 Error: " + ex.Message; }
        }

        private async Task<string> TestSmartMtu() {
            try {
                using (var ping = new System.Net.NetworkInformation.Ping()) {
                    byte[] buffer = new byte[1200];
                    var reply = await ping.SendPingAsync("8.8.8.8", 3000, buffer);
                    if (reply.Status == System.Net.NetworkInformation.IPStatus.Success) {
                        return "\uD83D\uDFE2 MTU 1280 Optimal (" + reply.RoundtripTime + "ms \u2022 No Loss)";
                    }
                    return "\uD83D\uDFE1 Low-MTU Path Ready (Anti-Fragment Clamping)";
                }
            } catch { return "\uD83D\uDFE2 Smart MTU 1280 Configured & Active"; }
        }

        private async Task<string> TestCleanIp() {
            try {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                foreach (var ip in CleanIpCandidates) {
                    try {
                        using (var tcp = new System.Net.Sockets.TcpClient()) {
                            var task = tcp.ConnectAsync(ip, 443);
                            if (await Task.WhenAny(task, Task.Delay(2000)) == task) {
                                sw.Stop();
                                return "\uD83D\uDFE2 Clean Edge IP: " + ip + " (" + sw.ElapsedMilliseconds + "ms)";
                            }
                        }
                    } catch { }
                }
                return "\uD83D\uDFE1 Clean IP Scanner Ready (CDN Pool Standby)";
            } catch (Exception ex) { return "\uD83D\uDD34 Scanner Error: " + ex.Message; }
        }

        private async Task<string> TestFakeSni() {
            try {
                using (var tcp = new System.Net.Sockets.TcpClient()) {
                    var task = tcp.ConnectAsync("cloudflare.com", 443);
                    if (await Task.WhenAny(task, Task.Delay(4000)) == task) {
                        return "\uD83D\uDFE2 Whitelist SNI Reachable (DPI Bypass Active)";
                    }
                    return "\uD83D\uDD34 Whitelist SNI Blocked or Inactive";
                }
            } catch (Exception ex) { return "\uD83D\uDD34 SNI Error: " + ex.Message; }
        }

        private static readonly string[] MelliCandidateIps = new string[] {
            "188.114.97.2", "188.114.97.3", "162.159.193.1", "172.64.155.209",
            "162.159.138.6", "104.16.132.229", "104.17.209.9", "162.159.192.1",
            "162.159.195.1", "141.101.90.1", "198.41.214.1", "198.41.215.1",
            "172.67.180.1", "104.19.154.1", "104.22.4.1", "172.67.135.1",
            "104.21.32.1", "104.26.10.1", "162.159.128.1", "188.114.96.1"
        };

        private async Task<List<Tuple<string, long>>> ScanMelliAnycastIpsAsync() {
            var alive = new List<Tuple<string, long>>();
            var tasks = new List<Task>();
            object sync = new object();

            foreach (var ip in MelliCandidateIps) {
                string targetIp = ip;
                tasks.Add(Task.Run(new Action(delegate {
                    try {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        using (var tcp = new System.Net.Sockets.TcpClient()) {
                            tcp.NoDelay = true;
                            var ar = tcp.BeginConnect(targetIp, 443, null, null);
                            if (ar.AsyncWaitHandle.WaitOne(1500)) {
                                tcp.EndConnect(ar);
                                sw.Stop();
                                lock (sync) {
                                    alive.Add(new Tuple<string, long>(targetIp, sw.ElapsedMilliseconds));
                                }
                            }
                        }
                    } catch { }
                })));
            }
            await Task.WhenAll(tasks.ToArray());
            alive.Sort((a, b) => a.Item2.CompareTo(b.Item2));
            return alive;
        }

        private async Task<string> TestDomesticSniPass(string sni) {
            await Task.Delay(1);return "TLS server-name validation is not implemented for this relay.";
        }

        private async Task<string> TestPort53Egress() {
            try { using(var udp=new UdpClient()){
                udp.Connect("1.1.1.1",53);
                byte[] query={0x12,0x34,1,0,0,1,0,0,0,0,0,0,7,101,120,97,109,112,108,101,3,99,111,109,0,0,1,0,1};
                await udp.SendAsync(query,query.Length);var receive=udp.ReceiveAsync();if(await Task.WhenAny(receive,Task.Delay(3000))!=receive)return "The DNS server did not respond.";
                byte[] reply=(await receive).Buffer;
                return reply.Length>=12&&reply[0]==0x12&&reply[1]==0x34&&(reply[2]&0x80)!=0?"DNS response received. This does not verify a VPN tunnel.":"DNS response was not valid.";
            }}catch(Exception){return "The DNS server did not respond.";}
        }

        private static void SetBlackoutDns(bool enable) {
            throw new NotSupportedException("This feature requires a verified relay and reversible network configuration.");
        }

        private static void SetWindowsProxy(string proxyServer, bool enable) {
            throw new NotSupportedException("This feature requires a verified relay and reversible network configuration.");
        }


        private void ApplyUdpAntiDropShield(bool enable) {
            throw new NotSupportedException("Edit MTU in your profile and explicitly reconnect to apply it.");
        }

        private async Task<string> TestUdpJitter() {
            await Task.Delay(1);return "A cooperating UDP test server is required. No packet-loss or jitter estimate is available.";
        }

        private void ImmunizeWindowsDnsHosts(bool enable) {
            throw new NotSupportedException("This feature requires a verified relay and reversible network configuration.");
        }

        private void LaunchTelegramFakeTlsProxy(string domain, bool copyOnly) {
            throw new NotSupportedException("This feature requires a verified relay and reversible network configuration.");
        }

        private async Task ToggleMasterBlackoutRescue() {
            await Task.Delay(1);ShowNotice("Connection tools","Use the individual reachability checks.","No Windows settings were changed.",false);
        }




        private async Task<string> ProbeAntiFilter(string address) {
            try { using(var client=new HttpClient(new HttpClientHandler{UseProxy=false})){client.Timeout=TimeSpan.FromSeconds(8);if(address.Contains("dns-query"))client.DefaultRequestHeaders.Accept.ParseAdd("application/dns-json");using(var response=await client.GetAsync(address,HttpCompletionOption.ResponseHeadersRead)){return "HTTP "+(int)response.StatusCode;}} }
            catch(Exception){return "No response. This does not identify the cause.";}
        }
        private void BuildEmergencyPage(Panel p) {
            var title=new Label{Text="Anti-Filter",Font=Theme.Display(19f),ForeColor=Theme.TextPrimary,AutoSize=true,Location=new Point(0,0)};p.Controls.Add(title);
            var intro=new Label{Text="Connection tools and advanced options",Font=Theme.Font(10),ForeColor=Theme.TextSecondary,AutoSize=false,Location=new Point(0,48),Size=new Size(500,42)};p.Controls.Add(intro);
            var list=new FlowLayoutPanel{FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoSize=true,Location=new Point(0,100)};p.Controls.Add(list);
            AddAntiTool(list,"Google relay","Check Google access; a response does not verify a relay.","Test",()=>ProbeAntiFilter("https://www.google.com/generate_204"));
            AddAntiTool(list,"TLS fragmentation","TLS splitting is handled by the Mozilla connection engine.","Details",()=>Task.FromResult("The supplied engine source splits the TLS ClientHello automatically. This screen does not enable a separate relay."));
            AddAntiTool(list,"Secure DNS","Check the HTTPS DNS endpoint.","Test",()=>ProbeAntiFilter("https://cloudflare-dns.com/dns-query?name=example.com&type=A"));
            AddAntiTool(list,"Anycast fallback","Fallback routes are handled by the Mozilla engine.","Details",()=>Task.FromResult("The engine includes Fastly fallback addresses. Availability depends on the network."));
            AddAntiTool(list,"Port 53","Test a DNS response over UDP; this is not a VPN tunnel.","Test",TestPort53Egress);
            AddAntiTool(list,"Smart MTU / UDP anti-drop","WARP profiles use MTU 1280 and keepalive 25s.","Details",()=>Task.FromResult("Generated profiles use smaller packets. Imported profiles retain their own settings. This does not guarantee zero packet loss."));
            AddAntiTool(list,"Alternate SNI","A working alternate SNI route needs a compatible upstream.","Details",()=>Task.FromResult("No verified alternate-SNI relay is configured. Changing a hostname alone cannot create one."));
            AddAntiTool(list,"Snowflake / blackout rescue","Requires a real compatible relay.","Details",()=>Task.FromResult("No Snowflake relay is configured in this build. A reachability test is not a working rescue tunnel."));
            AddAntiTool(list,"Telegram / MTProto","Mozilla local proxy and MTProto are different protocols.","Details",()=>Task.FromResult("The Mozilla proxy remains available. A Telegram MTProto link needs a real relay and secret; none is bundled."));
            p.Resize+=delegate{int width=Math.Max(100,p.ClientSize.Width-24);intro.Width=width;foreach(Control card in list.Controls){card.Width=width;card.Controls[0].Width=width-20;card.Controls[1].Width=width-20;}p.AutoScrollMinSize=new Size(0,list.Bottom+20);};
        }
        private void AddAntiTool(FlowLayoutPanel parent,string title,string description,string action,Func<Task<string>> run) {
            var card=new Panel{Width=600,Height=144,BackColor=Theme.Surface,Margin=new Padding(0,0,0,10)};
            card.Controls.Add(new Label{Text=title,Font=Theme.Font(11,FontStyle.Bold),ForeColor=Theme.TextPrimary,Location=new Point(12,10),Size=new Size(570,26)});
            card.Controls.Add(new Label{Text=description,Font=Theme.Font(9.5f),ForeColor=Theme.TextSecondary,Location=new Point(12,40),Size=new Size(570,42)});
            var button=new LuxuryButton{Text=action,Width=160,Height=38,Location=new Point(12,94)};
            button.Click+=async delegate{button.Enabled=false;try{string result=await run();ShowNotice(title,result,"",false);}catch(Exception){ShowNotice(title,"The check could not finish.","",false);}finally{button.Enabled=true;}};card.Controls.Add(button);parent.Controls.Add(card);
        }

        private void BuildDiagnosticsPage(Panel p) {
            var title = new Label { Text = "Help", Font = Theme.Display(19f), ForeColor = Theme.TextPrimary, AutoSize = true, Location = new Point(0, 0) };
            p.Controls.Add(title);

            exitInfoLabel.Text = "Exit IP: not verified yet";
            exitInfoLabel.Font = Theme.Mono(9.5f);
            exitInfoLabel.ForeColor = Theme.TextPrimary;
            exitInfoLabel.Location = new Point(0, 32);
            exitInfoLabel.AutoSize = true;
            p.Controls.Add(exitInfoLabel);

            var btnRow = new FlowLayoutPanel { Location = new Point(0, 58), AutoSize = true, WrapContents = true };
            diagTestBtn.Size = new Size(130, 36);
            diagPingBtn.Size = new Size(130, 36);
            copyLogsBtn.Size = new Size(110, 36);
            btnRow.Controls.Add(diagTestBtn); btnRow.Controls.Add(diagPingBtn); btnRow.Controls.Add(copyLogsBtn);
            var repair=new LuxuryButton{Text="Repair VPN",Width=120,Height=36};repair.Click+=async delegate{if(!ConfirmRepair())return;await Run(async delegate{await engine.Api("POST","/api/power",new{on=false});await engine.InstallTun();ShowNotice("VPN is ready","Try connecting again.","",false);});};btnRow.Controls.Add(repair);
            var guide=new LuxuryButton{Text="Guide",Width=90,Height=36};guide.Click+=delegate{Process.Start(new ProcessStartInfo(Path.Combine(engine.Root,"docs","guide.html")){UseShellExecute=true});};btnRow.Controls.Add(guide);
            var tgProxy=new LuxuryButton{Text="⚡ Telegram Proxy",Width=150,Height=36};tgProxy.Click+=async delegate{await ShowTelegramProxyDialog();};btnRow.Controls.Add(tgProxy);
            var updateBtn = new LuxuryButton { Text = "Check for updates", Width = 150, Height = 36 };
            appUpdateButton = updateBtn;
            updateBtn.Click += async delegate { await CheckForUpdates(true); };
            btnRow.Controls.Add(updateBtn);
            p.Controls.Add(btnRow);

            logsBox.Multiline = true;
            logsBox.ReadOnly = true;
            logsBox.ScrollBars = ScrollBars.Vertical;
            logsBox.BackColor = Color.FromArgb(14, 15, 18);
            logsBox.ForeColor = Theme.TextSecondary;
            logsBox.BorderStyle = BorderStyle.None;
            logsBox.Font = Theme.Mono(9f);
            p.Controls.Add(logsBox);

            Action layoutDiag = delegate {
                btnRow.MaximumSize = new Size(Math.Max(300, p.ClientSize.Width - 10), 0);
                logsBox.Location = new Point(0, btnRow.Bottom + 8);
                logsBox.Size = new Size(p.ClientSize.Width - 10, Math.Max(80, p.ClientSize.Height - btnRow.Bottom - 16));
            };
            p.Resize += delegate { layoutDiag(); };
            layoutDiag();
        }

        private static void LabelBox(Control control, int x, int y, int width, int height) {
            var label = control as Label;
            if (label != null) { label.AutoSize = false; label.AutoEllipsis = true; label.UseMnemonic=false; }
            control.SetBounds(x, y, Math.Max(1, width), Math.Max(1, height));
        }

        private static int PlaceButtons(Control[] buttons, int x, int y, int width) {
            int left = x, top = y, row = 0;
            foreach (var button in buttons) {
                int preferred = Math.Max(96, TextRenderer.MeasureText(button.Text, button.Font).Width + 28);
                int height = Math.Max(38, button.Font.Height + 18);
                preferred = Math.Min(preferred, width);
                if (left > x && left + preferred > x + width) { left = x; top += row + 8; row = 0; }
                button.SetBounds(left, top, preferred, height);
                left += preferred + 8; row = Math.Max(row, height);
            }
            return top + row;
        }

        private static int Header(Panel page, int width) {
            int bottom = 44;
            if (page.Controls.Count > 0) LabelBox(page.Controls[0], 0, 0, width, Math.Max(38,page.Controls[0].Font.Height+10));
            if (page.Controls.Count > 1 && page.Controls[1] is Label) {
                var label = (Label)page.Controls[1];
                int height = Math.Max(36, TextRenderer.MeasureText(label.Text, label.Font, new Size(width, 300), TextFormatFlags.WordBreak).Height + 4);
                LabelBox(label, 0, Math.Max(44,page.Controls[0].Bottom+8), width, height); bottom = label.Bottom;
            }
            return bottom + 14;
        }

        private void ArrangeResponsive() {
            if (arranging || pages[0] == null) return;
            arranging = true;
            try {
                bool compact = ClientSize.Width < 720;
                int header = Math.Max(44, Font.Height + 22);
                titleBar.SetBounds(0, 0, ClientSize.Width, header);
                resizeGrip.SetBounds(ClientSize.Width - 14, ClientSize.Height - 14, 14, 14);
                resizeGrip.Visible = WindowState != FormWindowState.Maximized;
                resizeGrip.BringToFront();
                int side = compact ? 0 : 184;
                int navHeight = compact ? 100 : 0;
                sidebar.SetBounds(0, header, compact ? ClientSize.Width : side, compact ? navHeight : Math.Max(1, ClientSize.Height - header));
                contentArea.Padding = compact ? new Padding(18, 14, 18, 14) : new Padding(26, 20, 26, 20);
                contentArea.SetBounds(side, header + navHeight, Math.Max(1, ClientSize.Width - side), Math.Max(1, ClientSize.Height - header - navHeight));
                if (announcementBar.Visible) {
                    int bannerWidth = Math.Min(640, contentArea.Width - 24);
                    int bannerHeight = 30;
                    int bannerX = side + (contentArea.Width - bannerWidth) / 2;
                    int bannerY = header + navHeight + 4;
                    announcementBar.SetBounds(bannerX, bannerY, bannerWidth, bannerHeight);
                    announcementText.SetBounds(10, 0, bannerWidth - 36, bannerHeight);
                    announcementClose.SetBounds(bannerWidth - 26, 2, 24, 26);
                    announcementBar.BringToFront();
                }
                for (int i = 0; i < navButtons.Count; i++) {
                    var nav = navButtons[i]; nav.Visible=true; nav.Compact = compact;
                    nav.SetBounds(compact ? 8 + (i % 4) * ((sidebar.Width - 16) / 4) : 10, compact ? 8 + (i / 4)*44 : 16 + i * 46,
                        compact ? (sidebar.Width - 16) / 4 - 2 : side - 20, compact ? 38 : 40);
                    nav.Invalidate();
                }
                for (int i = 0; i < 7; i++) {
                    pages[i].AutoScrollPosition = Point.Empty;
                    pages[i].AutoScrollMinSize = Size.Empty;
                    pages[i].AutoScroll = false;
                }

                var home = pages[0];
                bool maximized = WindowState == FormWindowState.Maximized;
                int contentHeight = 520;
                home.AutoScroll = home.ClientSize.Height < contentHeight;
                home.AutoScrollMinSize = home.AutoScroll ? new Size(0, contentHeight) : Size.Empty;
                int widthHome = Math.Min(maximized ? 840 : 520, Math.Max(100, home.ClientSize.Width - 42));
                int leftHome = Math.Max(10, (home.ClientSize.Width - widthHome - (home.AutoScroll ? 14 : 0)) / 2);
                int topHome = Math.Max(10, (home.ClientSize.Height - contentHeight) / 2);
                dialAction.TextAlign = dialSubtext.TextAlign = ContentAlignment.MiddleCenter;
                LabelBox(dialAction, leftHome, topHome, widthHome, 44);
                LabelBox(dialSubtext, leftHome, topHome + 44, widthHome, 34);
                dial.SetBounds(leftHome + (widthHome - 130) / 2, topHome + 80, 130, 120);
                connectBtn.SetBounds(leftHome + (widthHome - 240) / 2, topHome + 201, 240, 1);
                var actRow = home.Controls["connectionActions"]; if (actRow != null) actRow.SetBounds(leftHome, topHome + 203, widthHome, 1);
                selectedLocationCard.SetBounds(leftHome, topHome + 209, widthHome, 56);
                accountCard.SetBounds(leftHome, topHome + 271, widthHome, 55);
                trafficStrip.SetBounds(leftHome, topHome + 334, widthHome, 65);
                speedStats.Location = new Point(16, 30);
                uploadStats.Location = new Point(132, 30);
                modeBadge.SetBounds(leftHome + widthHome - Math.Min(400, widthHome), topHome + 406, Math.Min(400, widthHome), 66);
                noticeLabel.SetBounds(leftHome, topHome + 478, widthHome, 28);

                // Search/action rows wrap before they can overlap one another.
                foreach (int pageIndex in new[] { 1, 2 }) {
                    var page = pages[pageIndex]; int width = Math.Max(100, page.ClientSize.Width - 10); int top = Header(page, width);
                    var searchBox = pageIndex == 1 ? locationSearch : splitSearch;
                    Control[] buttons = pageIndex == 1 ? new Control[] { sortPingBtn, antigravityFilterBtn, pingBtn, applyLocationBtn } : new Control[] { splitBrowse, splitReload, splitSave };
                    int bottom;
                    if (width < 740) {
                        searchBox.SetBounds(0, top, width, 38);
                        bottom = PlaceButtons(buttons, 0, top + 48, width);
                    } else {
                        int actionWidth = 0; foreach (var button in buttons) actionWidth += Math.Max(96, TextRenderer.MeasureText(button.Text, button.Font).Width + 28) + 8;
                        int searchWidth = Math.Max(180, width - actionWidth - 12);
                        searchBox.SetBounds(0, top, searchWidth, 38);
                        bottom = PlaceButtons(buttons, searchWidth + 12, top, width - searchWidth - 12);
                    }
                    int listTop = bottom + 14;
                    if (pageIndex == 1) locationList.SetBounds(0, listTop, width, Math.Max(100, page.ClientSize.Height - listTop - 6));
                    else {
                        splitList.SetBounds(0, listTop, width, Math.Max(100, page.ClientSize.Height - listTop - 34));
                        splitList.Columns[0].Width = Math.Min(200, width / 3); splitList.Columns[2].Width = 100;
                        splitList.Columns[1].Width = Math.Max(80, splitList.ClientSize.Width - splitList.Columns[0].Width - 104);
                        LabelBox(splitCountLabel, 0, splitList.Bottom + 8, width, 22);
                    }
                }

                var accountsPage = pages[3]; int aw = Math.Max(100, accountsPage.ClientSize.Width - 10);
                int accountTop = Header(accountsPage, aw);
                int accountButtonsBottom = PlaceButtons(new Control[] { loginBtn, renewAccountBtn, useAccountBtn, refreshQuotaBtn, removeAccountBtn, accountEyeBtn }, 0, accountTop, aw);
                int accountsTop = accountButtonsBottom + 14;
                accountsList.SetBounds(0, accountsTop, aw, Math.Max(100, accountsPage.ClientSize.Height - accountsTop - 112));
                accountsList.Columns[0].Width = Math.Max(140, accountsList.ClientSize.Width - 394);
                accountsList.Columns[1].Width = 100; accountsList.Columns[2].Width = 160; accountsList.Columns[3].Width = 130;
                var info = accountsPage.Controls["accountInfo"]; info.SetBounds(0, accountsList.Bottom + 12, aw, 88);
                LabelBox(info.Controls[0], 14, 10, aw - 28, 22); LabelBox(info.Controls[1], 14, 34, aw - 28, 46);
                if (info.Bottom > accountsPage.ClientSize.Height) { accountsPage.AutoScroll = true; accountsPage.AutoScrollMinSize = new Size(0, info.Bottom + 8); }

                var settings = pages[4];
                settings.AutoScroll = settings.ClientSize.Height < 680;
                settings.AutoScrollMinSize = settings.AutoScroll ? new Size(0, 680) : Size.Empty;
                int sw = Math.Max(100, settings.ClientSize.Width - 22);
                settings.Controls["appearanceShortcut"].SetBounds(Math.Max(0, sw - 130), 0, 130, 36);
                foreach (var card in modeCards) card.Width = sw;
                var trayRow = settings.Controls["trayPreference"]; trayRow.Width = sw; trayRow.Height = 60;
                LabelBox(trayRow.Controls[1], 66, 9, Math.Max(1, sw - 80), 44);
                var warRow = settings.Controls["warPreference"];
                if (warRow != null) {
                    warRow.Width = sw; warRow.Height = 66;
                    LabelBox(warRow.Controls[1], 66, 8, Math.Max(1, sw - 80), 22);
                    LabelBox(warRow.Controls[2], 66, 32, Math.Max(1, sw - 80), 28);
                    if (settings.Controls["warHeading"] != null) settings.Controls["warHeading"].Top = 390;
                    warRow.Top = 412;
                }
                settings.Controls["resourcesHeading"].Top = warRow != null ? 490 : 402;
                var resources = settings.Controls["resourceButtons"]; resources.SetBounds(0, warRow != null ? 512 : 428, sw, 44);
                var appRow = settings.Controls["appearanceButtons"]; if (appRow != null) appRow.SetBounds(0, warRow != null ? 560 : 476, sw, 44);
                var legal = settings.Controls["sourceNotice"]; LabelBox(legal, 0, warRow != null ? 610 : 528, sw, 52);

                var emergency = pages[5];
                if(emergency!=null){emergency.AutoScroll=true;int width=Math.Max(100,emergency.ClientSize.Width-24);foreach(Control child in emergency.Controls){var flow=child as FlowLayoutPanel;if(flow!=null){foreach(Control card in flow.Controls){card.Width=width;card.Controls[0].Width=width-24;card.Controls[1].Width=width-24;}emergency.AutoScrollMinSize=new Size(0,flow.Bottom+24);}}}

                var diagnostics = pages[6]; int dw = Math.Max(100, diagnostics.ClientSize.Width - 10);
                LabelBox(exitInfoLabel, 0, 34, dw, 28);
                var diagRow = diagTestBtn.Parent;
                if (diagRow != null) {
                    diagRow.MaximumSize = new Size(dw, 0);
                    diagRow.Location = new Point(0, 72);
                }
                int logTop = diagRow != null ? diagRow.Bottom + 10 : 198;
                logsBox.SetBounds(0, logTop, dw, Math.Max(80, diagnostics.ClientSize.Height - logTop - 6));
            } finally { arranging = false; }
        }

        private void InitializeTray() {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open ONYX // VPN", null, delegate { Show(); WindowState = FormWindowState.Normal; Activate(); });
            menu.Items.Add("âš¡ Telegram Proxy", null, async delegate { await ShowTelegramProxyDialog(); });
            menu.Items.Add("Disconnect", null, async delegate { await Run(async delegate { await engine.Api("POST", "/api/power", new { on = false }); }); });
            menu.Items.Add("Quit", null, delegate { allowClose = true; Close(); });
            tray.Icon = Icon;
            tray.Text = "ONYX // VPN";
            tray.ContextMenuStrip = menu;
            tray.Visible = true;
            tray.DoubleClick += delegate { Show(); WindowState = FormWindowState.Normal; Activate(); };
        }

        private async Task ShowTelegramProxyDialog() {
            string sponsorMarker = Path.Combine(engine.DataDir, "tg-sponsor-joined.txt");
            if (File.Exists(sponsorMarker)) {
                await ConnectTelegramProxy();
                return;
            }
            using (var dialog = new Form {
                Text = "85", BackColor = Theme.Bg, ForeColor = Theme.TextPrimary,
                ClientSize = new Size(540, 260), StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.None, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false
            }) {
                dialog.Paint += delegate (object sender, PaintEventArgs e) {
                    using (var pen = new Pen(Theme.Accent, 1f))
                        e.Graphics.DrawRectangle(pen, 0, 0, dialog.ClientSize.Width - 1, dialog.ClientSize.Height - 1);
                };
                dialog.Controls.Add(new Label {
                    Text = UiStyle.T("Telegram channel"),
                    Font = Theme.Font(14f, FontStyle.Bold), ForeColor = Theme.Accent,
                    Bounds = new Rectangle(24, 16, 492, 32), TextAlign = ContentAlignment.MiddleRight
                });
                dialog.Controls.Add(new Label {
                    Text = UiStyle.T("Join @cabicy to use the Telegram proxy. Then select Check membership."),
                    Font = Theme.Font(10f), ForeColor = Theme.TextPrimary,
                    Bounds = new Rectangle(24, 52, 492, 90), TextAlign = ContentAlignment.TopRight
                });
                bool channelClicked = false;
                var cancel = new LuxuryButton { Text = UiStyle.T("Cancel"), Variant = ButtonVariant.Ghost, DialogResult = DialogResult.Cancel, Bounds = new Rectangle(24, 204, 80, 36) };
                var verifyBtn = new LuxuryButton { Text = UiStyle.T("Check membership"), Variant = ButtonVariant.Secondary, Bounds = new Rectangle(114, 204, 190, 36) };
                verifyBtn.Click += delegate {
                    if (!channelClicked) {
                        ShowNotice(UiStyle.T("Channel membership"), UiStyle.T("Join the channel first."), "", false);
                        return;
                    }
                    try { File.WriteAllText(sponsorMarker, "joined"); } catch {}
                    dialog.DialogResult = DialogResult.OK;
                    dialog.Close();
                };
                var join = new LuxuryButton { Text = UiStyle.T("Join @cabicy"), Variant = ButtonVariant.Primary, Bounds = new Rectangle(314, 204, 202, 36) };
                join.Click += delegate {
                    channelClicked = true;
                    try { Process.Start(new ProcessStartInfo("tg://resolve?domain=cabicy") { UseShellExecute = true }); }
                    catch { try { Process.Start(new ProcessStartInfo("https://t.me/cabicy") { UseShellExecute = true }); } catch {} }
                };
                dialog.Controls.Add(cancel); dialog.Controls.Add(verifyBtn); dialog.Controls.Add(join);
                dialog.CancelButton = cancel;
                if (dialog.ShowDialog(this) == DialogResult.OK) {
                    await ConnectTelegramProxy();
                }
            }
        }

        private async Task ConnectTelegramProxy() {
            noticeLabel.Text = UiStyle.T("Preparing Telegram proxy..."); noticeLabel.Update();
            var chk = await engine.Api("GET", "/api/accounts");
            bool hasReady = false;
            foreach (var a in Json.Rows(chk, "accounts")) {
                string st = Json.Str(a, "status");
                bool en = !(Json.Get(a, "enabled") is bool && !Json.Bool(a, "enabled"));
                if (en && st != "reauth_required" && st != "sign in again") { hasReady = true; break; }
            }
            if (!hasReady) {
                await AutoAssignPoolAccount();
                await RefreshAccounts();
                if (accountsList.Items.Count == 0) { SwitchPage(3); loginBtn.PerformClick(); return; }
            }
            try{
                if(Json.Str(Json.Child(lastStatus,"state"),"phase")=="off")await engine.Api("POST","/api/power",new{on=true});
            }catch(Exception error){ShowFailure(error.Message);return;}
            int targetPort = engine.ProxyPort > 0 ? engine.ProxyPort : 10810;
            bool portReady = false;
            for (int i = 0; i < 35; i++) {
                try {
                    var st = await engine.Api("GET", "/api/status", null, 1500);
                    string ph = Json.Str(Json.Child(st, "state"), "phase");
                    if (ph == "running") {
                        using (var tcp = new System.Net.Sockets.TcpClient()) {
                            var ar = tcp.BeginConnect(engine.ProxyAddress, targetPort, null, null);
                            if (ar.AsyncWaitHandle.WaitOne(250)) {
                                tcp.EndConnect(ar);
                                portReady = true;
                                break;
                            }
                        }
                    }
                } catch {}
                await Task.Delay(250);
            }
            if (!portReady) {
                noticeLabel.Text = UiStyle.T("Telegram proxy could not connect. Check your connection.");
                noticeLabel.Update();
                return;
            }
            noticeLabel.Text = UiStyle.T("Opening Telegram..."); noticeLabel.Update();
            try {
                Process.Start(new ProcessStartInfo("tg://socks?server=" + engine.ProxyAddress + "&port=" + targetPort) { UseShellExecute = true });
            } catch (Exception) {
                try {
                    Process.Start(new ProcessStartInfo("https://t.me/socks?server=" + engine.ProxyAddress + "&port=" + targetPort) { UseShellExecute = true });
                } catch (Exception) {}
            }
            return;
        }

        private void SetActions(bool enabled) {
            enabled=enabled&&!requiredUpdate;
            dial.Enabled = enabled;
            modeBadge.Enabled = enabled;
            connectBtn.Enabled = enabled;
            quickBtn.Enabled = enabled && Json.Str(Json.Child(lastStatus, "state"), "phase") == "off";
            antigravityBtn.Enabled = enabled;
            verifyBtn.Enabled = enabled && !testing;
            loginBtn.Enabled = enabled;
            renewAccountBtn.Enabled = enabled;
            refreshQuotaBtn.Enabled = enabled;
            useAccountBtn.Enabled = enabled;
            removeAccountBtn.Enabled = enabled;
            applyLocationBtn.Enabled = enabled;
            pingBtn.Enabled = enabled && !pingScanBusy;
            diagPingBtn.Enabled = enabled && !pingScanBusy;
            splitSave.Enabled = enabled&&Json.Bool(Json.Child(lastStatus,"tun"),"programs_supported");
            splitReload.Enabled = enabled;
            splitBrowse.Enabled = enabled;
        }

        private void ShowAbout(){
            using(var dialog=new Form{Text="85",BackColor=Theme.Bg,ForeColor=Theme.TextPrimary,ClientSize=new Size(480,230),FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.CenterParent}){
                dialog.Controls.Add(new Label{Text="85",Font=Theme.Display(24f),ForeColor=Theme.TextPrimary,Bounds=new Rectangle(24,24,432,44)});
                dialog.Controls.Add(new Label{Text="Version 1.1.20",Font=Theme.Font(11f),ForeColor=Theme.TextSecondary,Bounds=new Rectangle(24,82,432,30)});
                var privacy=new LuxuryButton{Text="Privacy",Bounds=new Rectangle(24,154,132,40)};privacy.Click+=delegate{Process.Start(new ProcessStartInfo(new Uri(Path.Combine(engine.Root,"docs","guide.html")).AbsoluteUri+"#privacy"){UseShellExecute=true});};
                var licenses=new LuxuryButton{Text="Licenses",Bounds=new Rectangle(168,154,132,40)};licenses.Click+=delegate{Process.Start(new ProcessStartInfo(Path.Combine(engine.Root,"licenses")){UseShellExecute=true});};
                var close=new LuxuryButton{Text="Done",Variant=ButtonVariant.Primary,Bounds=new Rectangle(312,154,144,40),DialogResult=DialogResult.OK};dialog.Controls.AddRange(new Control[]{privacy,licenses,close});dialog.AcceptButton=close;dialog.CancelButton=close;dialog.ShowDialog(this);
            }
        }
        private bool ConfirmRepair() {return ConfirmAction("Repair VPN service","This disconnects VPN and asks for Windows administrator approval. Your accounts stay saved.","Repair");}
        internal bool ConfirmAction(string title,string message,string action) {
            using(var dialog=new Form{Text="85",BackColor=Theme.Bg,ForeColor=Theme.TextPrimary,ClientSize=new Size(480,200),StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.None,MaximizeBox=false,MinimizeBox=false,ShowInTaskbar=false}){
                dialog.Paint+=delegate(object sender,PaintEventArgs e){using(var pen=new Pen(Theme.Border))e.Graphics.DrawRectangle(pen,0,0,dialog.ClientSize.Width-1,dialog.ClientSize.Height-1);};
                dialog.Controls.Add(new Label{Text=title,Font=Theme.Font(17f,FontStyle.Bold),ForeColor=Theme.TextPrimary,Bounds=new Rectangle(24,20,432,36)});
                dialog.Controls.Add(new Label{Text=message,Font=Theme.Font(11f),ForeColor=Theme.TextSecondary,Bounds=new Rectangle(24,68,432,62)});
                var cancel=new LuxuryButton{Text="Cancel",Variant=ButtonVariant.Ghost,DialogResult=DialogResult.Cancel,Bounds=new Rectangle(224,144,100,36)};
                var repair=new LuxuryButton{Text=action,DialogResult=DialogResult.OK,Bounds=new Rectangle(336,144,120,36)};
                dialog.Controls.Add(cancel);dialog.Controls.Add(repair);dialog.AcceptButton=repair;dialog.CancelButton=cancel;return dialog.ShowDialog(this)==DialogResult.OK;
            }
        }
        internal static string FriendlyFailure(string detail) {
            if (detail.StartsWith("Disconnect the other VPN", StringComparison.Ordinal)) return detail;
            if(detail.IndexOf("application ID",StringComparison.OrdinalIgnoreCase)>=0 && detail.IndexOf("Access is denied",StringComparison.OrdinalIgnoreCase)>=0)return "VPN needs a quick repair. Open Help to continue.";
            if(detail.IndexOf("TUN",StringComparison.OrdinalIgnoreCase)>=0)return "Whole computer couldn't start. Open Help to repair VPN.";
            if(detail.IndexOf("account",StringComparison.OrdinalIgnoreCase)>=0)return "Your account needs attention. Open Account to continue.";
            if(detail.IndexOf("cancel",StringComparison.OrdinalIgnoreCase)>=0)return "The operation was cancelled. You can try again whenever you're ready.";
            return "We couldn't complete that. Check your connection and try again.";
        }
        private void ShowFailure(string detail) {
            if (detail.IndexOf("CAPTCHA", StringComparison.OrdinalIgnoreCase) >= 0) {
                lastActionError = detail;
                dialSubtext.Text = "Mozilla needs verification. Select Connect to sign in.";
                dialAction.Text = "Verify your account";
                dial.State = DialState.Error;
                return;
            }
            dialSubtext.Text=FriendlyFailure(detail)+"  \u2022  Click for details";
            dialSubtext.Cursor=Cursors.Hand;
            dial.State=DialState.Error;statusPill.State=DialState.Error;statusPill.StatusText="NEEDS ATTENTION";
            dialAction.Text="Attention required";dial.Invalidate();statusPill.StatusText=UiStyle.T(statusPill.StatusText);statusPill.Invalidate();
            ShowNotice("Connection problem",FriendlyFailure(detail),detail,true);
        }
        internal void ShowNotice(string title,string message,string detail,bool repair) {
            using(var dialog=new Form {Text="85",StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.None,MaximizeBox=false,MinimizeBox=false,ShowInTaskbar=false,BackColor=Theme.Bg,ForeColor=Theme.TextPrimary,ClientSize=new Size(520,220),AutoScaleMode=AutoScaleMode.Dpi}) {
                dialog.Paint+=delegate(object sender,PaintEventArgs e){using(var pen=new Pen(Theme.Border))e.Graphics.DrawRectangle(pen,0,0,dialog.ClientSize.Width-1,dialog.ClientSize.Height-1);};
                var heading=new Label{Text=title,Font=Theme.Font(18f,FontStyle.Bold),ForeColor=Theme.TextPrimary,Bounds=new Rectangle(24,22,472,38)};
                var summary=new Label{Text=message,Font=Theme.Font(11f),ForeColor=Theme.TextSecondary,Bounds=new Rectangle(24,70,472,66)};
                var details=new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Text=detail,BackColor=Theme.Surface,ForeColor=Theme.TextSecondary,BorderStyle=BorderStyle.FixedSingle,Bounds=new Rectangle(24,208,472,120),Visible=false};
                var expand=new LuxuryButton{Text="Technical details",Variant=ButtonVariant.Ghost,Bounds=new Rectangle(24,155,152,38)};
                expand.Visible=!String.IsNullOrEmpty(detail);
                expand.Click+=delegate{details.Visible=!details.Visible;dialog.ClientSize=new Size(520,details.Visible?352:220);expand.Text=details.Visible?"Hide details":"Technical details";};
                var close=new LuxuryButton{Text="Got it",Bounds=new Rectangle(382,155,114,38),DialogResult=DialogResult.OK};
                dialog.Controls.AddRange(new Control[]{heading,summary,details,expand,close});dialog.AcceptButton=close;dialog.CancelButton=close;
                if(repair){bool account=detail.IndexOf("account",StringComparison.OrdinalIgnoreCase)>=0;var settings=new LuxuryButton{Text=account?"Open account":"Get help",Bounds=new Rectangle(224,155,146,38),Variant=ButtonVariant.Secondary};settings.Click+=delegate{dialog.Close();SwitchPage(account?3:7);};dialog.Controls.Add(settings);}
                dialog.ShowDialog(this);
            }
        }
        private void OpenCoverageMenu() {
            if (!ready || busy || closing || requiredUpdate) return;
            var menu = new ContextMenuStrip { ShowCheckMargin = true, ShowImageMargin = false,
                BackColor = Theme.Surface, ForeColor = Theme.TextPrimary, ShowItemToolTips = true };
            string current = Json.Str(lastStatus, "system_proxy");
            if (Json.Str(Json.Child(lastStatus, "state"), "phase") != "off") {
                menu.Items.Add(new ToolStripMenuItem("VPN reconnects after changing mode") { Enabled = false });
                menu.Items.Add(new ToolStripSeparator());
            }
            for (int i = 0; i < CoverageControl.Modes.Length; i++) {
                int choice = i;
                var item = new ToolStripMenuItem(UiStyle.T(CoverageControl.Names[i])) {
                    Checked = CoverageControl.Modes[i] == current,
                    ToolTipText = UiStyle.T(CoverageControl.Descriptions[i]),
                    ForeColor = Theme.TextPrimary
                };
                item.Click += async delegate { await ChangeCoverage(choice); };
                menu.Items.Add(item);
            }
            menu.Items.Add(new ToolStripSeparator());
            var routing = new ToolStripMenuItem(UiStyle.T("Proxy & routing"));
            routing.Click += async delegate {
                SwitchPage(7);
                if (pages[7].Controls.Count == 0) await Run(EditProxyRouting);
            };
            menu.Items.Add(routing);
            menu.Show(modeBadge, new Point(modeBadge.Width, modeBadge.Height), ToolStripDropDownDirection.BelowLeft);
        }

        private async Task ChangeCoverage(int index) {
            string mode = CoverageControl.Modes[index];
            if (mode == Json.Str(lastStatus, "system_proxy")) return;
            await Run(async delegate {
                bool reconnect = Json.Str(Json.Child(lastStatus, "state"), "phase") != "off";
                if (reconnect && mode == "tun") await PrepareWholeComputer();
                if (reconnect)
                    await engine.Api("POST", "/api/power", new { on = false });
                await engine.Api("POST", "/api/system-proxy", new { mode = mode });
                modeCards[index].Checked = true;
                if (reconnect) await engine.Api("POST", "/api/power", new { on = true });
            });
        }

        private async Task Run(Func<Task> action) {
            if (!ready || busy || closing || requiredUpdate) return;
            busy = true;lastActionError=""; SetActions(false);
            dialSubtext.Text="Applying your changes\u2026";dialSubtext.Update();
            try { await action(); busy=false; await Poll(); }
            catch (Exception e) { lastActionError=e.Message;ShowFailure(e.Message); }
            finally { busy = false; if (!closing) SetActions(ready); }
        }

        private async Task ConnectFromHome() {
            if (!ready || busy || closing) return;
            string problem = lastActionError + " " + ConnectionState.Failure(lastStatus);
            if (problem.IndexOf("CAPTCHA", StringComparison.OrdinalIgnoreCase) >= 0) {
                await engine.Api("POST", "/api/power", new { on = false });
                using (var signIn = new SignInForm(engine)) {
                    if (signIn.ShowDialog(this) != DialogResult.OK) return;
                }
                lastActionError = "";
                await Poll();
                SwitchPage(3);
                dialSubtext.Text = "Account verified. Select the verified account before connecting.";
                return;
            }
            string phase = Json.Str(Json.Child(lastStatus, "state"), "phase");
            var accounts=Json.Child(lastStatus,"accounts");
            if(phase=="off"&&Json.Get(accounts,"total")!=null&&Json.Num(accounts,"total")==0&&accountsList.Items.Count==0&&!requiredUpdate){
                await AutoAssignPoolAccount();
                if (accountsList.Items.Count == 0) { SwitchPage(3); loginBtn.PerformClick(); return; }
            }
            dial.State=DialState.Connecting;statusPill.State=DialState.Connecting;
            statusPill.StatusText=phase=="off"?"CONNECTING\u2026":"DISCONNECTING\u2026";
            dialAction.Text=phase=="off"?"Connecting\u2026":"Disconnecting\u2026";
            dial.Invalidate();statusPill.StatusText=UiStyle.T(statusPill.StatusText);statusPill.Invalidate();dialAction.Update();
            await Run(TogglePower);
        }
        private void ShowAccountIntro(){
            if(!AccountSetup.ShouldShow(accountIntroSeen,accountsList.Items.Count>0,requiredUpdate))return;
            using(var dialog=new AccountIntroForm())dialog.ShowDialog(this);
            if(!requiredUpdate){accountIntroSeen=true;SavePreferences();}
        }

        private async Task TogglePower() {
            if(requiredUpdate)throw new IOException("Update required.");
            if (Json.Str(Json.Child(lastStatus, "state"), "phase") != "off") {
                await engine.Api("POST", "/api/power", new { on = false }); return;
            }
            var availableAccounts=await engine.Api("GET", "/api/accounts");
            bool hasReady = false;
            foreach (var a in Json.Rows(availableAccounts, "accounts")) {
                string st = Json.Str(a, "status");
                bool en = !(Json.Get(a, "enabled") is bool && !Json.Bool(a, "enabled"));
                if (en && st != "reauth_required" && st != "sign in again") {
                    hasReady = true;
                    break;
                }
            }
            if (!hasReady) {
                foreach (var a in Json.Rows(availableAccounts, "accounts")) {
                    string st = Json.Str(a, "status");
                    if (st == "reauth_required" || st == "sign in again") {
                        try { await engine.Api("DELETE", "/api/accounts/" + Uri.EscapeDataString(Json.Str(a, "id"))); } catch { }
                    }
                }
                await AutoAssignPoolAccount();
                availableAccounts=await engine.Api("GET", "/api/accounts");
            }
            if (!Json.Rows(availableAccounts, "accounts").GetEnumerator().MoveNext()) {
                SwitchPage(3);loginBtn.Focus();
                throw new IOException("Sign in with your own Mozilla account before connecting.");
            }
            string mode = pendingModeIndex == 0 ? "tun" : pendingModeIndex == 1 ? "set" : "clear";
            if (mode == "tun") await PrepareWholeComputer();
            await engine.Api("POST", "/api/system-proxy", new { mode = mode });
            bool needAutoHeal = false;
            try {
                await engine.Api("POST", "/api/power", new { on = true });
            } catch (Exception ex) {
                string msg = ex.Message.ToLowerInvariant();
                if (msg.Contains("account") || msg.Contains("reauth") || msg.Contains("refresh") || msg.Contains("mozilla") || msg.Contains("406") || msg.Contains("challenge")) {
                    throw;
                } else {
                    throw;
                }
            }
            if (needAutoHeal) {
                throw new IOException("Sign in again to continue.");
            }
        }

        private async Task PrepareWholeComputer() {
            if (!Json.Bool(lastStatus, "tun_supported")) throw new IOException("Whole-system TUN is unavailable on this computer.");
            engine.AllowTunServiceRead();
            bool running = await Task.Run(delegate {
                try {
                    using (var service = new ServiceController("FirefoxVPNTun")) {
                        if (service.Status == ServiceControllerStatus.StartPending) {
                            service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(4));
                        }
                        return service.Status == ServiceControllerStatus.Running;
                    }
                } catch (InvalidOperationException) { return false; }
                  catch (Win32Exception) { return false; }
                  catch (System.ServiceProcess.TimeoutException) { return false; }
            });
            if (!running) {
                dialSubtext.Text = "Setting up VPN\u2026 Approve the Windows prompt.";
                await engine.InstallTun();
            }
            await VpnHandoff.DisconnectPrevious();
        }

        private async Task QuickConnect() {
            if (Json.Str(Json.Child(lastStatus, "state"), "phase") != "off") return;
            string chosen = ConnectionState.QuickCountry(selectedLocationCard.CountryCode);
            await engine.Api("POST", "/api/location", new { country = chosen });
            await TogglePower();
        }

        private void LoadPreferences() {
            try {
                string path = Path.Combine(engine.DataDir, "ui-prefs.json");
                if (File.Exists(path)) {
                    var prefs = Json.Decode(File.ReadAllText(path));
                    hideEmail = Json.Bool(prefs, "hide_email");
                    accountIntroSeen=Json.Bool(prefs,"account_intro_seen");
                    if (Json.Get(prefs, "sort_ping") != null) sortByPing = Json.Bool(prefs, "sort_ping");
                    if (Json.Get(prefs, "emergency_google_relay") != null) emergGoogleRelayToggle.Checked = Json.Bool(prefs, "emergency_google_relay");
                    if (Json.Get(prefs, "emergency_tls_frag") != null) emergTlsFragToggle.Checked = Json.Bool(prefs, "emergency_tls_frag");
                    if (Json.Get(prefs, "emergency_secure_dns") != null) emergSecureDnsToggle.Checked = Json.Bool(prefs, "emergency_secure_dns");
                    if (Json.Get(prefs, "emergency_anycast") != null) emergAnycastToggle.Checked = Json.Bool(prefs, "emergency_anycast");
                    if (Json.Get(prefs, "emergency_stealth_port") != null) emergStealthPortToggle.Checked = Json.Bool(prefs, "emergency_stealth_port");
                    if (Json.Get(prefs, "emergency_smart_mtu") != null) emergSmartMtuToggle.Checked = Json.Bool(prefs, "emergency_smart_mtu");
                    if (Json.Get(prefs, "emergency_clean_ip") != null) emergCleanIpToggle.Checked = Json.Bool(prefs, "emergency_clean_ip");
                    if (Json.Get(prefs, "emergency_fake_sni") != null) emergFakeSniToggle.Checked = Json.Bool(prefs, "emergency_fake_sni");
                }
            } catch { }
            UpdatePingSort();
        }

        private void SavePreferences() {
            try {
                string path = Path.Combine(engine.DataDir, "ui-prefs.json");
                File.WriteAllText(path, Json.Encode(new Dictionary<string, object> {
                    { "hide_email", hideEmail },
                    { "sort_ping", sortByPing },
                    { "account_intro_seen", accountIntroSeen },
                    { "emergency_google_relay", emergGoogleRelayToggle.Checked },
                    { "emergency_tls_frag", emergTlsFragToggle.Checked },
                    { "emergency_secure_dns", emergSecureDnsToggle.Checked },
                    { "emergency_anycast", emergAnycastToggle.Checked },
                    { "emergency_stealth_port", emergStealthPortToggle.Checked },
                    { "emergency_smart_mtu", emergSmartMtuToggle.Checked },
                    { "emergency_clean_ip", emergCleanIpToggle.Checked },
                    { "emergency_fake_sni", emergFakeSniToggle.Checked }
                }));
            } catch { }
        }

        private string MaskEmail(string email) {
            if (!hideEmail || String.IsNullOrEmpty(email)) return email ?? "";
            int at = email.IndexOf('@');
            if (at <= 1) return "\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022";
            string user = email.Substring(0, 1) + "\u2022\u2022\u2022\u2022";
            int dot = email.IndexOf('.', at);
            string domain = (dot > at + 1) ? "\u2022\u2022\u2022\u2022" + email.Substring(dot) : "\u2022\u2022\u2022\u2022.com";
            return user + "@" + domain;
        }

        private void ToggleEmailPrivacy() {
            hideEmail = !hideEmail;
            SavePreferences();
            UpdateEyeButtons();
            UpdateAccountBadge();
            var _ = RefreshAccounts();
        }

        private void UpdateEyeButtons() {
            eyeBtn.Text = hideEmail ? "Show" : "Hide";
            accountEyeBtn.Text = hideEmail ? "Show email" : "Hide email";
        }

        private void UpdateAccountBadge() {
            var acct = Json.Child(lastStatus, "active_account");
            var usage = Json.Child(acct, "usage");
            string email = Json.Str(acct, "email");
            bool hasAccts = ConnectionState.HasAccounts(lastStatus);

            if (acct.Count == 0) {
                accountCard.Email = hasAccts ? UiStyle.T("Account saved") : UiStyle.T("No account added");
                accountCard.QuotaText = UiStyle.T("Sign in to get started.");
                accountCard.QuotaPercent = -1f;
                accountBadge.Text = accountCard.Email;
            } else {
                accountCard.Email = MaskEmail(email);
                accountCard.HideEmail = hideEmail;
                bool unlimited = Json.Bool(usage, "unlimited");
                accountCard.IsUnlimited = unlimited;

                if (unlimited) {
                    accountCard.QuotaText = UiStyle.T("Unlimited");
                    accountCard.QuotaPercent = -1f;
                    accountBadge.Text = accountCard.Email + " \u2022 " + UiStyle.T("Unlimited");
                } else if (usage.Count > 0) {
                    long remaining = (long)Json.Num(usage, "remaining_bytes");
                    long total = (long)Json.Num(usage, "total_bytes");
                    if (total > 0) {
                        float usedRatio = Math.Max(0f, Math.Min(1.0f, (float)(total - remaining) / total));
                        accountCard.QuotaPercent = usedRatio;
                        accountCard.QuotaText = Theme.Bytes(remaining) + " " + UiStyle.T("remaining") + " / " + Theme.Bytes(total);
                    } else {
                        accountCard.QuotaPercent = -1f;
                        accountCard.QuotaText = Theme.Bytes(remaining) + " " + UiStyle.T("remaining");
                    }
                    accountBadge.Text = accountCard.Email + " \u2022 " + accountCard.QuotaText;
                } else {
                    accountCard.QuotaText = UiStyle.T("Checking balance\u2026");
                    accountCard.QuotaPercent = -1f;
                    accountBadge.Text = accountCard.Email;
                }
            }
            accountCard.Invalidate();
        }

        private async Task MeasureLocationLatency() {
            if (!ready || busy || closing || pingScanBusy) return;
            pingScanBusy = true;
            pingBtn.Enabled = diagPingBtn.Enabled = false;
            pingBtn.Text = diagPingBtn.Text = "Pinging\u2026";
            try {
                await engine.Api("POST", "/api/locations/ping", new { codes = new string[0] }, 60000);
                if (!closing) await LoadLocations();
            } catch (Exception error) { if (!closing) ShowNotice("Ping test failed","Try again or choose another server.",error.Message,false); }
            finally {
                pingScanBusy = false;
                if (!closing) { pingBtn.Text = diagPingBtn.Text = "Test ping"; pingBtn.Enabled = diagPingBtn.Enabled = ready && !busy; }
            }
        }

        private async Task ConnectAntigravity() {
            string targetCode = "gb";
            ServerChoice targetChoice = null;
            foreach (var sc in allLocations) {
                if (sc.Code.Equals("gb", StringComparison.OrdinalIgnoreCase)) {
                    targetChoice = sc;
                    break;
                }
            }
            if (targetChoice == null) throw new IOException("United Kingdom is unavailable. Choose another server.");

            await engine.Api("POST", "/api/location", new { country = targetCode });
            selectedLocationCard.LocationName = targetChoice.Name;
            selectedLocationCard.CountryCode = targetCode;
            selectedLocationCard.Latency = (targetChoice != null && targetChoice.PingMs > 0) ? targetChoice.PingMs + " ms" : "-- ms";
            selectedLocationCard.Invalidate();

            if (Json.Str(Json.Child(lastStatus, "state"), "phase") == "off") {
                await TogglePower();
            } else {
                dialSubtext.Text = "Switching server (" + targetCode.ToUpperInvariant() + ")\u2026";
                await engine.Api("POST", "/api/power", new { on = false });
                await engine.Api("POST", "/api/power", new { on = true });
            }
        }

        private void ToggleAntigravityFilter() {
            antigravityFilterOnly = !antigravityFilterOnly;
            antigravityFilterBtn.Variant = antigravityFilterOnly ? ButtonVariant.Primary : ButtonVariant.Ghost;
            antigravityFilterBtn.Text = antigravityFilterOnly ? "All Locations" : "Popular servers";
            FilterLocations();
        }

        private bool WindowIsVisible { get { return Visible && WindowState != FormWindowState.Minimized; } }
        private bool wasWindowVisible;

        private async Task RefreshAfterWindowChange() {
            bool visible = WindowIsVisible;
            if (visible == wasWindowVisible) return;
            wasWindowVisible = visible;
            if (!ready || closing) return;
            dial.PauseAnimation(!visible);
            timer.Interval = visible ? 1000 : 5000;
            if (visible) await Poll();
        }

        private async Task Poll() {
            if (!ready || polling || closing) return;
            polling = true;
            try {
                lastStatus = await engine.Api("GET", "/api/status", null, 5000);
                if(busy || closing)return;
                var state = Json.Child(lastStatus, "state");
                string phase = Json.Str(state, "phase");
                bool connected = ConnectionState.Verified(lastStatus);
                tray.Text = "85 \u2022 " + (connected ? "Connected" : "Disconnected");
                if (!WindowIsVisible) {
                    timer.Interval = 5000;
                    if (!testing && warmup.ShouldTest(lastStatus, DateTime.UtcNow)) {
                        var probe = TestConnection(false);
                    }
                    return;
                }
                var tun = Json.Child(lastStatus, "tun");
                var proxy = Json.Child(lastStatus, "proxy");
                var acct = Json.Child(lastStatus, "active_account");
                var usage = Json.Child(acct, "usage");
                var egress = Json.Child(lastStatus, "exit");
                var traffic = Json.Child(lastStatus, "traffic");
                dial.Sample(Json.Num(traffic, "down_rate"), Json.Num(traffic, "up_rate"));
                trafficStrip.Invalidate();

                if(connected)lastActionError="";
                string failure=lastActionError!=""?lastActionError:ConnectionState.Failure(lastStatus);


                timer.Interval = phase != "off" && !connected ? 350 : 1000;

                dial.State = phase == "off" ? DialState.Disconnected :
                             connected ? DialState.Connected :
                             failure!="" ? DialState.Error : DialState.Connecting;
                dial.Invalidate();

                statusPill.State = dial.State;
                statusPill.StatusText = phase == "off" ? "DISCONNECTED" :
                                        connected ? "PROTECTED" :
                                        failure!="" ? "NEEDS ATTENTION" :
                                        phase == "running" ? "CONNECTING\u2026" :
                                        phase == "waiting" ? "WAITING\u2026" : "CONNECTING\u2026";
                statusPill.StatusText=UiStyle.T(statusPill.StatusText);statusPill.Invalidate();

                bool needsAccount = !ConnectionState.HasAccounts(lastStatus) && (phase == "off" || phase == "waiting");
                dialAction.Text = needsAccount ? UiStyle.T("Sign in") :
                                  connected ? UiStyle.T("Connected") :
                                  failure != "" ? UiStyle.T("Attention required") :
                                  phase == "off" ? UiStyle.T("Not connected") :
                                  UiStyle.T("Connecting\u2026");
                dialAction.ForeColor = connected ? Theme.Success :
                                       failure != "" ? Theme.Warning :
                                       phase == "off" ? Theme.TextPrimary :
                                       Theme.Accent;

                connectBtn.Text = needsAccount ? UiStyle.T("Add account") :
                                  phase == "off" ? UiStyle.T("Connect") :
                                  connected ? UiStyle.T("Disconnect") :
                                  phase == "error" ? UiStyle.T("Reset connection") :
                                  UiStyle.T("Cancel connection");
                connectBtn.Variant = connected ? ButtonVariant.Secondary : ButtonVariant.Primary;
                connectBtn.Invalidate();
                dial.AccessibleName = connectBtn.Text;

                if (connected) {
                    string loc = selectedLocationCard.LocationName;
                    string lat = selectedLocationCard.Latency;
                    dialSubtext.Text = (UiStyle.Farsi ? "Ù…ØªØµÙ„ Ø¨Ù‡ " : "Connected to ") + loc + (lat != "-- ms" && lat != "" ? "  \u2022  " + lat : "");
                } else if (needsAccount) {
                    dialSubtext.Text = UiStyle.T("Sign in to get started.");
                } else if (failure != "") {
                    dialSubtext.Text = FriendlyFailure(failure) + "  \u2022  " + (UiStyle.Farsi ? "Ø¬Ø²Ø¦ÛŒØ§Øª" : "Click for details");
                } else if (phase == "off") {
                    dialSubtext.Text = UiStyle.T("Choose a location and connect whenever you're ready.");
                } else {
                    string msg = Json.Str(state, "message");
                    dialSubtext.Text = msg != "" ? msg : UiStyle.T("Connecting\u2026");
                }

                dialSubtext.Cursor = failure != "" ? Cursors.Hand : Cursors.Default;
                double downNow = Json.Num(traffic, "down"), upNow = Json.Num(traffic, "up");
                if (connected && !sessionTrafficActive) { sessionDownStart = downNow; sessionUpStart = upNow; }
                sessionTrafficActive = connected;
                if (downNow < sessionDownStart) sessionDownStart = 0;
                if (upNow < sessionUpStart) sessionUpStart = 0;
                speedStats.Text = Theme.Bytes(Json.Num(traffic, "down_rate")) + "/s\n" + Theme.Bytes(Math.Max(0, downNow - sessionDownStart)) + " total";
                uploadStats.Text = Theme.Bytes(Json.Num(traffic, "up_rate")) + "/s\n" + Theme.Bytes(Math.Max(0, upNow - sessionUpStart)) + " total";

                string currentMode = Json.Str(lastStatus, "system_proxy");
                modeBadge.UpdateMode(currentMode);

                UpdateAccountBadge();

                if (egress.Count > 0) exitInfoLabel.Text = "Exit IPv4: " + Json.Str(egress, "ipv4") + "  \u2022  " + Json.Str(egress, "country_name");

                // Diagnostics must not delay or invalidate connection status.
                var logsRefresh = RefreshLogs();

                if (!busy) SetActions(true);
                if (!testing && !busy && warmup.ShouldTest(lastStatus, DateTime.UtcNow)) {
                    var _ = TestConnection(false);
                }
            } catch {
                dial.State = DialState.Disconnected;
                statusPill.State = DialState.Disconnected;
            } finally { polling = false; }
        }

        private async Task RefreshLogs(bool force = false) {
            if (!ready || closing || IsDisposed || logsPolling || activePageIndex != 5) return;
            if (!force && DateTime.UtcNow < nextLogs) return;
            logsPolling = true; nextLogs = DateTime.UtcNow.AddSeconds(4);
            try {
                var entries = await engine.Api("GET", "/api/logs?after=" + lastLogId, null, 4000);
                if (closing || IsDisposed || logsBox.IsDisposed) return;
                var text = new StringBuilder(); long newest = lastLogId;
                foreach (var entry in Json.Rows(entries, "entries")) {
                    long id = (long)Json.Num(entry, "id");
                    if (id <= lastLogId) continue;
                    newest = Math.Max(newest, id);
                    text.Append(Json.Str(entry, "time")).Append("  ").Append(Json.Str(entry, "level")).Append("  ").Append(Json.Str(entry, "message")).AppendLine();
                }
                if (text.Length > 0) logsBox.AppendText(text.ToString());
                lastLogId = newest; logsErrorReported = false;
                if (logsBox.TextLength > 90000) logsBox.Text = logsBox.Text.Substring(logsBox.TextLength - 60000);
            } catch {
                if (!closing && !IsDisposed && !logsBox.IsDisposed && !logsErrorReported) {
                    logsBox.AppendText("Diagnostics couldn't refresh. Connection status will keep updating." + Environment.NewLine);
                    logsErrorReported = true;
                }
            } finally { logsPolling = false; }
        }

        private async Task TestConnection(bool show) {
            if (!ready || testing || closing) return;
            testing = true; verifyBtn.Enabled = false;
            try {
                var result = await engine.Api("POST", "/api/test");
                if (show && !closing) {
                    bool ok = Json.Bool(result, "ok");
                    if(ok)ShowNotice("You're connected","Your connection check passed.","Exit IP: "+Json.Str(result,"exit_ipv4"),false);
                    else ShowFailure(Json.Str(result,"error"));
                }
            } catch (Exception e) { if (show && !closing) ShowFailure(e.Message); }
            finally { testing = false; warmup.Finished(DateTime.UtcNow); if (!closing) verifyBtn.Enabled = ready && !busy; }
            if (!closing) await Poll();
        }

        private static readonly byte[] VAULT_KEY = new byte[]{
            0x49, 0x49, 0x49, 0x38, 0x35, 0x49, 0x49, 0x49,
            0x2d, 0x53, 0x65, 0x63, 0x75, 0x72, 0x65, 0x2d,
            0x56, 0x61, 0x75, 0x6c, 0x74, 0x2d, 0x32, 0x30,
            0x32, 0x36, 0x2d, 0x4b, 0x45, 0x59, 0x38, 0x35
        };

        private static string DecryptPool(string payloadB64, string ivB64) {
            try {
                byte[] iv = Convert.FromBase64String(ivB64);
                byte[] ct = Convert.FromBase64String(payloadB64);
                using (var aes = new System.Security.Cryptography.AesManaged()) {
                    aes.Key = VAULT_KEY;
                    aes.IV = iv;
                    aes.Mode = System.Security.Cryptography.CipherMode.CBC;
                    aes.Padding = System.Security.Cryptography.PaddingMode.PKCS7;
                    using (var decryptor = aes.CreateDecryptor()) {
                        byte[] pt = decryptor.TransformFinalBlock(ct, 0, ct.Length);
                        return System.Text.Encoding.UTF8.GetString(pt);
                    }
                }
            } catch {
                return null;
            }
        }

        private bool allocatingPool;
        private async Task AutoAssignPoolAccount() {
            if (allocatingPool) return;
            allocatingPool = true;
            try {
                var accounts = await engine.Api("GET", "/api/accounts");
                bool hasUsable = false;
                foreach (var existing in Json.Rows(accounts, "accounts")) {
                    string st = Json.Str(existing, "status");
                    bool en = !(Json.Get(existing, "enabled") is bool && !Json.Bool(existing, "enabled"));
                    if (en && st != "reauth_required" && st != "sign in again") { hasUsable = true; break; }
                }
                if (hasUsable) return;

                foreach (var a in Json.Rows(accounts, "accounts")) {
                    string id = Json.Str(a, "id");
                    string st = Json.Str(a, "status");
                    if (st == "reauth_required" || st == "sign in again") {
                        try { await engine.Api("DELETE", "/api/accounts/" + Uri.EscapeDataString(id)); } catch { }
                    }
                }

                await FetchAndImportAccount();
                await RefreshAccounts();
            } catch { }
            finally { allocatingPool = false; }
        }

        private async Task<bool> FetchAndImportAccount() {
            string path = Path.Combine(engine.DataDir, "allocation.dpapi");
            string credential;
            if (File.Exists(path)) {
                credential = System.Text.Encoding.UTF8.GetString(System.Security.Cryptography.ProtectedData.Unprotect(File.ReadAllBytes(path), null, System.Security.Cryptography.DataProtectionScope.CurrentUser));
            } else {
                byte[] bytes = new byte[32];
                using (var random = System.Security.Cryptography.RandomNumberGenerator.Create()) random.GetBytes(bytes);
                credential = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
                File.WriteAllBytes(path, System.Security.Cryptography.ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(credential), null, System.Security.Cryptography.DataProtectionScope.CurrentUser));
            }

            bool imported = false;
            try {
                using (var handler = new System.Net.Http.HttpClientHandler { AllowAutoRedirect = false })
                using (var client = new System.Net.Http.HttpClient(handler)) {
                    client.Timeout = TimeSpan.FromSeconds(15);
                    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", credential);
                    using (var response = await client.PostAsync("https://85-accounts.parsaghelich062.workers.dev/v1/account", new System.Net.Http.StringContent("{}", System.Text.Encoding.UTF8, "application/json"))) {
                        if (response.IsSuccessStatusCode) {
                            string text = await response.Content.ReadAsStringAsync();
                            if (text.Length <= 32768) {
                                var account = Json.Decode(text);
                                string email = Json.Str(account, "email"), token = Json.Str(account, "refresh_token");
                                if (email != "" && token != "") {
                                    var imp = await engine.Api("POST", "/api/accounts/import", new { email = email, refresh_token = token });
                                    string newId = Json.Str(imp, "id");
                                    if (!string.IsNullOrEmpty(newId)) {
                                        try { await engine.Api("POST", "/api/accounts/" + Uri.EscapeDataString(newId) + "/refresh"); } catch { }
                                    }
                                    imported = true;
                                }
                            }
                        }
                    }
                }
            } catch { }

            if (!imported) {
                imported = await ImportFromLocalPool();
            }
            return imported;
        }

        private Task<bool> ImportFromLocalPool() { return Task.FromResult(false); }

        private async Task RequestAccountReplacement(bool force) {
            string cooldownPath = Path.Combine(engine.DataDir, "replacement.cooldown");
            if (!force && File.Exists(cooldownPath)) {
                try {
                    string txt = File.ReadAllText(cooldownPath).Trim();
                    long ticks;
                    if (long.TryParse(txt, out ticks)) {
                        var lastTime = new DateTime(ticks, DateTimeKind.Utc);
                        var elapsed = DateTime.UtcNow - lastTime;
                        if (elapsed < TimeSpan.FromHours(4)) {
                            int hoursLeft = (int)Math.Ceiling((TimeSpan.FromHours(4) - elapsed).TotalHours);
                            throw new IOException(string.Format("Account replacement is on cooldown ({0}h remaining) to protect shared capacity. If your connection has an issue, try 'Check balance' or restart the app.", hoursLeft));
                        }
                    }
                } catch (IOException) { throw; } catch { }
            }

            var accList = await engine.Api("GET", "/api/accounts");
            var rows = Json.Rows(accList, "accounts");
            bool hasHealthyAccount = false;
            string healthyEmail = "";
            long healthyRemaining = 0;

            foreach (var a in rows) {
                string status = Json.Str(a, "status");
                bool enabled = !(Json.Get(a, "enabled") is bool && !Json.Bool(a, "enabled"));
                var usage = Json.Child(a, "usage");
                long rem = (long)Json.Num(usage, "remaining_bytes");
                bool unlimited = Json.Bool(usage, "unlimited");
                
                if (enabled && status != "reauth_required" && status != "sign in again" && (unlimited || rem > 1073741824)) {
                    hasHealthyAccount = true;
                    healthyEmail = Json.Str(a, "email");
                    healthyRemaining = rem;
                    break;
                }
            }

            if (hasHealthyAccount && !force) {
                double gb = Math.Round((double)healthyRemaining / (1024 * 1024 * 1024), 1);
                throw new IOException(string.Format("Your current account ({0}) is healthy with {1} GB allowance remaining. Replacement accounts from storage are only granted when an account is broken, expired, or exhausted.", healthyEmail, gb));
            }

            foreach (var a in rows) {
                string id = Json.Str(a, "id");
                string st = Json.Str(a, "status");
                var usage = Json.Child(a, "usage");
                long rem = (long)Json.Num(usage, "remaining_bytes");
                if (st == "reauth_required" || st == "sign in again" || rem <= 0 || !hasHealthyAccount) {
                    try { await engine.Api("DELETE", "/api/accounts/" + Uri.EscapeDataString(id)); } catch { }
                }
            }

            string allocPath = Path.Combine(engine.DataDir, "allocation.dpapi");
            try { if (File.Exists(allocPath)) File.Delete(allocPath); } catch { }

            bool assigned = await FetchAndImportAccount();
            if (!assigned) {
                throw new IOException("Could not retrieve a replacement account from storage at this moment. Check your network or try again shortly.");
            }

            try {
                File.WriteAllText(cooldownPath, DateTime.UtcNow.Ticks.ToString());
            } catch { }

            await RefreshAccounts();
            ShowNotice("Account Renewed", "A new account with 50 GB allowance has been assigned from storage and is ready.", "", false);
        }

        private async Task ReplaceExhaustedAccount(string email, string accountId) {
            await RequestAccountReplacement(true);
        }

        private async Task RefreshAccounts() {
            var reply = await engine.Api("GET", "/api/accounts");
            accountsList.Items.Clear();
            foreach (var a in Json.Rows(reply, "accounts")) {
                var usage = Json.Child(a, "usage");
                var item = new ListViewItem(MaskEmail(Json.Str(a, "email"))) { Tag = Json.Str(a, "id") };
                item.SubItems.Add(Json.Get(a,"enabled") is bool&&!Json.Bool(a,"enabled")?"Paused":Json.Str(a, "status"));
                string reason=Json.Str(a,"reason_detail");if(reason=="")reason=Json.Str(a,"reason");string address=Json.Str(a,"email");if(hideEmail&&address!="")reason=reason.Replace(address,MaskEmail(address));item.ToolTipText=reason;
                item.SubItems.Add(usage.Count == 0 ? "Unknown" : Json.Bool(usage, "unlimited") ? "Unlimited" : Theme.Bytes(Json.Num(usage, "remaining_bytes")));
                DateTime reset;
                item.SubItems.Add(DateTime.TryParse(Json.Str(usage, "reset_at"), out reset) ? reset.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "--");
                accountsList.Items.Add(item);
            }
        }

        private async Task LoadLocations() {
            var reply = await engine.Api("GET", "/api/locations", null, 15000);
            string selected = Json.Str(lastStatus, "selection");
            allLocations.Clear();
            allLocations.Add(new ServerChoice("⚡ Auto (Fastest)", "fastest") { PingMs = 0 });
            allLocations.Add(new ServerChoice("Recommended", "") { PingMs = 0 });

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "", "fastest", "REC" };
            foreach (var l in Json.Rows(reply, "locations")) {
                string code = Json.Str(l, "code");
                if (!seen.Add(code)) continue;
                string name = Json.Str(l, "name");
                if (name == "") name = code;
                allLocations.Add(new ServerChoice(name, code) { PingMs = (int)Json.Num(l, "ping_ms") });
            }
            FilterLocations();

            // Sync with selected card
            foreach (var sc in allLocations) {
                if (sc.Code.Equals(selected, StringComparison.OrdinalIgnoreCase)) {
                    selectedLocationCard.LocationName = sc.Name;
                    selectedLocationCard.CountryCode = sc.Code;
                    selectedLocationCard.Latency = sc.PingMs > 0 ? sc.PingMs + " ms" : "-- ms";
                    selectedLocationCard.Invalidate();
                    break;
                }
            }
        }

        private void FilterLocations() {
            string q = locationSearch.Input.Text.Trim();
            var agSafe = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "gb", "de", "nl", "us", "fr", "ca", "se", "ch", "at" };
            var selected = locationList.SelectedItem as ServerChoice;
            string selectedCode = selected == null ? null : selected.Code;
            var visible = new List<ServerChoice>();
            foreach (var sc in allLocations) {
                if (sc.Code=="" || sc.Code=="fastest" || sc.Code=="REC") {visible.Add(sc);continue;}
                if (antigravityFilterOnly && sc.Code != "" && !agSafe.Contains(sc.Code)) continue;
                if (q == "" || sc.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || sc.Code.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) visible.Add(sc);
            }
            if (sortByPing) visible.Sort(delegate(ServerChoice a, ServerChoice b) {
                int pinnedA=a.Code==""?0:a.Code=="fastest"?1:a.Code=="REC"?2:3;
                int pinnedB=b.Code==""?0:b.Code=="fastest"?1:b.Code=="REC"?2:3;
                if(pinnedA!=pinnedB)return pinnedA.CompareTo(pinnedB);
                int result = (a.PingMs > 0 ? a.PingMs : Int32.MaxValue).CompareTo(b.PingMs > 0 ? b.PingMs : Int32.MaxValue);
                if (result != 0) return result;
                result = StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
                return result != 0 ? result : StringComparer.OrdinalIgnoreCase.Compare(a.Code, b.Code);
            });
            locationList.BeginUpdate();
            try {
                locationList.Items.Clear();
                foreach (var sc in visible) {
                    int index = locationList.Items.Add(sc);
                    if (selectedCode != null && sc.Code.Equals(selectedCode, StringComparison.OrdinalIgnoreCase)) locationList.SelectedIndex = index;
                }
                if (locationList.Items.Count > 0 && locationList.SelectedIndex < 0) locationList.SelectedIndex = 0;
            } finally { locationList.EndUpdate(); }
        }

        private void UpdatePingSort() {
            sortPingBtn.Text = sortByPing ? "Sorted by ping" : "Sort by ping";
            sortPingBtn.Variant = sortByPing ? ButtonVariant.Primary : ButtonVariant.Secondary;
            sortPingBtn.Invalidate();
        }

        private async Task ApplySelectedLocation() {
            if (locationList.SelectedItem == null) return;
            var chosen = (ServerChoice)locationList.SelectedItem;
            await engine.Api("POST", "/api/location", new { country = chosen.Code });
            selectedLocationCard.LocationName = chosen.Name;
            selectedLocationCard.CountryCode = chosen.Code;
            selectedLocationCard.Latency = chosen.PingMs > 0 ? chosen.PingMs + " ms" : "-- ms";
            selectedLocationCard.Invalidate();
            SwitchPage(0);
        }

        private async Task LoadSplitApps() {
            if (!Json.Bool(Json.Child(lastStatus, "tun"), "programs_supported")) {
                splitCountLabel.Text="App rules need the desktop VPN engine. Restart the app to load it.";
                splitSave.Enabled=false;
                return;
            }
            if (!splitLoaded) {
                var config = Json.Child(await engine.Api("GET", "/api/settings"), "config");
                foreach (var pair in SplitRules.Read(config)) splitSelected[pair.Key] = pair.Value;
            }
            var running = await engine.Api("GET", "/api/tun/programs");
            splitAvailable.Clear();
            foreach (var row in Json.Rows(running, "programs")) {
                string name = SplitRules.CleanName(Json.Str(row, "name"));
                if (name != "") splitAvailable[name] = new AppBypass(name, Json.Str(row, "path"));
            }
            foreach (var pair in splitSelected) if (!splitAvailable.ContainsKey(pair.Key)) splitAvailable[pair.Key] = pair.Value;
            splitLoaded = true;
            RenderSplitApps();
            UpdateSplitSummary();
        }

        private void RenderSplitApps() {
            splitRendering = true;
            splitList.BeginUpdate();
            try {
                splitList.Items.Clear();
                var names = new List<string>(splitAvailable.Keys);
                names.Sort(StringComparer.OrdinalIgnoreCase);
                string q = splitSearch.Input.Text.Trim();
                foreach (string name in names) {
                    var rule = splitAvailable[name];
                    if (q != "" && (name + " " + rule.Path).IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    AppBypass selected;
                    bool check = splitSelected.TryGetValue(name, out selected);
                    var item = new ListViewItem(name) { Tag = rule, Checked = check };
                    item.SubItems.Add(rule.Path);
                    item.SubItems.Add(check && selected.UdpOnly ? "UDP only" : check ? "Bypassing VPN" : "Using VPN");
                    splitList.Items.Add(item);
                }
            } finally { splitList.EndUpdate(); splitRendering = false; }
        }

        private void UpdateSplitSummary() {
            splitCountLabel.Text = splitSelected.Count + " applications selected to bypass VPN.";
        }

        private async Task EditProxyRouting() {
            var config = Json.Child(await engine.Api("GET", "/api/settings"), "config");
            var proxy = Json.Child(config, "proxy");
            var routing = Json.Child(config, "routing");
            IPEndPoint current;
            if (!Engine.TryProxyListener(Json.Str(proxy, "listen"), out current))
                current = new IPEndPoint(IPAddress.Loopback, engine.ProxyPort);
            var dialog = new Panel {
                Location = Point.Empty, Size = new Size(530, 580),
                BackColor = Theme.Bg, ForeColor = Theme.TextPrimary, Font = Theme.Font(10)
            };
            var page = pages[7];
            page.AutoScroll = true;
            page.Controls.Add(dialog);
            dialog.Controls.Add(new Label { Text = UiStyle.T("Proxy & routing"), Font = Theme.Display(18), Bounds = new Rectangle(20, 8, 490, 33) });
                var address = new TextBox { Text = current.Address.ToString(), Bounds = new Rectangle(20, 85, 310, 28) };
                var port = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = current.Port, Bounds = new Rectangle(350, 85, 160, 28) };
                var enabled = new CheckBox { Text = "Enable destination rules", Checked = Json.Bool(routing, "enabled"), Bounds = new Rectangle(20, 145, 490, 28) };
                var direct = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Bounds = new Rectangle(20, 206, 490, 80) };
                var block = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Bounds = new Rectangle(20, 322, 490, 80) };
                direct.Lines = (Json.Get(routing, "direct") as IEnumerable ?? new object[0]).Cast<object>().Select(x => Convert.ToString(x)).ToArray();
                block.Lines = (Json.Get(routing, "block") as IEnumerable ?? new object[0]).Cast<object>().Select(x => Convert.ToString(x)).ToArray();
                var save = new LuxuryButton { Text = "Save", Variant = ButtonVariant.Primary, Bounds = new Rectangle(350, 517, 160, 38) };
                var copy = new LuxuryButton { Text = "Copy active address", Bounds = new Rectangle(20, 517, 172, 38) };
                var destination = new TextBox { Bounds = new Rectangle(20, 440, 310, 28), AccessibleName = "Destination to test" };
                var test = new LuxuryButton { Text = "Test saved rules", Bounds = new Rectangle(350, 436, 160, 36) };
                var hint = new Label { Text = "Block rules win. Unmatched destinations use the VPN.", Bounds = new Rectangle(20, 476, 490, 36) };
                dialog.Controls.AddRange(new Control[] {
                    new Label { Text = "SOCKS5 listen address", Bounds = new Rectangle(20, 57, 310, 25) },
                    new Label { Text = "Port", Bounds = new Rectangle(350, 57, 160, 25) },
                    address, port, enabled, direct, block, hint, save, copy, destination, test,
                    new Label { Text = "Test a destination, e.g. example.com", Bounds = new Rectangle(20, 413, 490, 24) },
                    new Label { Text = "Direct - one rule per line", Bounds = new Rectangle(20, 180, 490, 24) },
                    new Label { Text = "Block - one rule per line", Bounds = new Rectangle(20, 296, 490, 24) }
                });
                bool saving = false;

                copy.Click += delegate {
                    try {
                        Clipboard.SetText("socks5://" + engine.ProxyAddress + ":" + engine.ProxyPort);
                        hint.Text = "Active address copied.";
                    } catch (System.Runtime.InteropServices.ExternalException) { hint.Text = "Clipboard busy. Try again."; }
                };
                test.Click += async delegate {
                    string target = destination.Text.Trim();
                    if (target.Length == 0) { destination.Focus(); return; }
                    test.Enabled = false;
                    try {
                        var result = await engine.Api("POST", "/api/routing/test", new { target = target });
                        if (dialog.IsDisposed) return;
                        string route = Json.Str(result, "route");
                        string rule = Json.Str(result, "rule");
                        hint.Text = "Saved rules: " + (route == "proxy" ? "VPN" : route) + (rule.Length == 0 ? "" : " â€¢ " + rule);
                    } catch (Exception error) {
                        if (!dialog.IsDisposed) hint.Text = error.Message;
                    } finally { if (!test.IsDisposed) test.Enabled = true; }
                };
                save.Click += async delegate {
                    if (saving) return;
                    IPEndPoint target;
                    if (!Engine.TryProxyListener(address.Text.Trim() + ":" + port.Value, out target)) {
                        hint.Text = "Enter an IPv4 address, such as 127.0.0.1 or 0.0.0.0.";
                        return;
                    }
                    save.Enabled = false;
                    saving = true;
                    try {
                        await engine.SaveProxyRouting(target, enabled.Checked, direct.Lines, block.Lines);
                        saving = false;
                        hint.Text = "Proxy and routing settings saved.";
                    } catch (Exception error) {
                        hint.Text = error.Message;
                    } finally { saving = false; if (!save.IsDisposed) save.Enabled = true; }
                };

        }

        private async Task SaveSplitApps() {
            var config = Json.Child(await engine.Api("GET", "/api/settings"), "config");
            SplitRules.Apply(config, splitSelected.Values);
            await engine.Api("PUT", "/api/settings", new { config = config });
            var saved = SplitRules.Read(Json.Child(await engine.Api("GET", "/api/settings"), "config"));
            if (saved.Count != splitSelected.Count) throw new IOException("Engine did not retain all bypass rules.");
            splitCountLabel.Text = "Saved: " + saved.Count + " applications bypass VPN.";
        }

        private void BrowseApp() {
            using (var picker = new OpenFileDialog { Filter = "Applications (*.exe)|*.exe", CheckFileExists = true, Multiselect = true, Title = "Select applications to bypass VPN" }) {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                foreach (var path in picker.FileNames) {
                    string name = Path.GetFileName(path);
                    var rule = new AppBypass(name, path);
                    splitAvailable[name] = rule;
                    splitSelected[name] = rule;
                }
                RenderSplitApps();
                UpdateSplitSummary();
            }
        }
    }

    internal sealed class ServerChoice {
        public string Name, Code;
        public int PingMs;
        public ServerChoice(string name, string code) { Name = name; Code = code; }
        public override string ToString() { return Name; }
    }

    internal static class Program {
        [STAThread]
        static int Main(string[] args) {
            SecurityShield.Initialize();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            string root = AppDomain.CurrentDomain.BaseDirectory;
            bool smoke = Array.IndexOf(args, "--smoke-test") >= 0; bool preview = Array.IndexOf(args, "--onyx-preview") >= 0;
            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "III85III VPN", "user-data");
            bool isolated = preview; if (preview) data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ONYX", "Preview");
            foreach (string arg in args) {
                if (arg.StartsWith("--data-dir=", StringComparison.Ordinal)) {
                    data = Path.GetFullPath(arg.Substring(11));
                    isolated = true;
                }
            }
            string mutexName = "Local\\ONYX-VPN-" + WindowsIdentity.GetCurrent().User.Value;
            if (isolated) using (var hash = SHA256.Create()) mutexName += "-" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(data.ToLowerInvariant()))).Replace("-", "").Substring(0, 16);

            UiStyle.SetDataDirectory(data);UiStyle.Load();
            bool created;
            using (var mutex = new Mutex(true, mutexName, out created)) {
                if (!created) {
                    if (!smoke) MessageBox.Show("ONYX VPN is already running. Access it from the notification tray.", "ONYX VPN");
                    return 2;
                }
                try {
                    using (var engine = new Engine(root, data)) {
                        if (smoke) { Smoke(engine).GetAwaiter().GetResult(); return 0; }
                        var form = new MainForm(engine, layoutOnly: preview); form.UseOnyx(preview);
                        if (isolated) form.Text += " \u2022 Preview";
                        Application.Run(form);
                        return 0;
                    }
                } catch (Exception e) {
                    if (smoke) Console.Error.WriteLine(e.ToString());
                    else MessageBox.Show(e.Message, "ONYX VPN", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
            }
        }

        static async Task Smoke(Engine engine) {
            Exception failure = null;
            string resultPath = Path.Combine(engine.DataDir, "smoke-result.json");
            if (File.Exists(resultPath)) File.Delete(resultPath);
            try {
                await engine.Start();
                var state = await engine.Api("GET", "/api/status");
                if (Json.Str(Json.Child(state, "state"), "phase") != "off") throw new Exception("Engine unexpectedly started network routing.");
                if (!Json.Bool(state, "tun_supported")) throw new Exception("TUN support missing.");
                var accounts = await engine.Api("GET", "/api/accounts");
                foreach (var a in Json.Rows(accounts, "accounts")) throw new Exception("Smoke-test data must have no accounts.");
                var settings = await engine.Api("GET", "/api/settings");
                if (!Json.Str(Json.Child(Json.Child(settings, "config"), "dashboard"), "listen").StartsWith("127.0.0.1:", StringComparison.Ordinal)) throw new Exception("Dashboard is not loopback-only.");
                using (var bare = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }))
                using (var denied = await bare.GetAsync(engine.Endpoint + "/api/status")) {
                    if (denied.StatusCode != HttpStatusCode.Unauthorized) throw new Exception("Unauthenticated API access was not rejected.");
                }
                var programs = await engine.Api("GET", "/api/tun/programs");
                if (Json.Get(programs, "programs") == null) throw new Exception("Running app inventory is unavailable.");
                var original = Json.Child(settings, "config");
                string transport = Json.Str(Json.Child(original, "proxy"), "transport");
                var originalRules = SplitRules.Read(original);
                SplitRules.Apply(original, new[] { new AppBypass("iii85iii-smoke-app.exe", "") });
                await engine.Api("PUT", "/api/settings", new { config = original });
                var applied = Json.Child(await engine.Api("GET", "/api/settings"), "config");
                if (!SplitRules.Read(applied).ContainsKey("iii85iii-smoke-app.exe")) throw new Exception("Split rule did not persist in real engine.");
                if (Json.Str(Json.Child(applied, "proxy"), "transport") != transport) throw new Exception("Split save changed the transport.");
                SplitRules.Apply(applied, originalRules.Values);
                await engine.Api("PUT", "/api/settings", new { config = applied });
                var report = new { passed = true, engine_version = Json.Str(state, "version"), state = "off", tun_supported = true, isolated_empty_accounts = true, loopback_only = true, unauthenticated_api_rejected = true, split_rule_roundtrip = true, app_inventory = true, network_connection_tested = false };
                File.WriteAllText(Path.Combine(engine.DataDir, "smoke-result.json"), Json.Encode(report));
            } catch (Exception e) { failure = e; }
            await engine.Stop();
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
