Vendored from https://github.com/MihaZupan/HttpToSocks5Proxy archived branch.
Pinned revision: f595aa19b000025ee53081b8607db29c26740afa.
License: MIT; included in Licenses/HttpToSocks5Proxy.txt.

This upstream is archived. The NET48 experiment owns review and maintenance of this copy.
Local change: bind the internal HTTP listener to IPAddress.Loopback instead of IPAddress.Any.
Routes retain bridge instances until process shutdown so active transfers survive proxy configuration changes.
LauncherProxy.Shutdown stops all bridge listeners. No Windows proxy setting is changed.
TLS is still performed and validated by the Framework HTTP client; no certificate validation is disabled.
