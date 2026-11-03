using MM.Core;
using MM.Gameplay;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace MM.Net
{
    // On the NetworkManager object. Starts Host / Client / Server with the right addresses,
    // auto-starts the dedicated server build, and gates joins through Connection Approval (server side).
    [RequireComponent(typeof(NetworkManager), typeof(UnityTransport))]
    public sealed class NetworkBootstrap : MonoBehaviour
    {
        // On EC2 the server must listen on every interface; the default (localhost) is unreachable from outside.
        const string k_ListenAll = "0.0.0.0";

        public const string RejectFull = "full";
        public const string RejectMatchInProgress = "match in progress";

        [SerializeField] NetworkManager m_NetworkManager;
        [SerializeField] UnityTransport m_Transport;

        public NetworkManager NetworkManager => m_NetworkManager;

        public static bool IsDedicatedServerBuild
        {
            get
            {
#if UNITY_SERVER
                return true;
#else
                return false;
#endif
            }
        }

        static GameConfig Config => GameConfig.Current;

        void Awake()
        {
            if (m_NetworkManager == null) m_NetworkManager = GetComponent<NetworkManager>();
            if (m_Transport == null) m_Transport = GetComponent<UnityTransport>();
        }

        void Start()
        {
            m_NetworkManager.ConnectionApprovalCallback = ApproveConnection;
            m_NetworkManager.OnClientConnectedCallback += OnClientConnected;
            m_NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            m_NetworkManager.OnTransportFailure += OnTransportFailure;

            if (IsDedicatedServerBuild || Config.ForceServer)
                StartServer();
        }

        void OnDestroy()
        {
            if (m_NetworkManager == null) return;
            m_NetworkManager.ConnectionApprovalCallback = null;
            m_NetworkManager.OnClientConnectedCallback -= OnClientConnected;
            m_NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            m_NetworkManager.OnTransportFailure -= OnTransportFailure;
        }

        // forceOverrideCommandLineArgs = true everywhere: UnityTransport would otherwise parse -ip/-port itself;
        // GameConfig already did, and one source of truth is easier to debug.

        public bool StartHost()
        {
            // Listen on all interfaces so a second PC on the same LAN can join the host too.
            m_Transport.SetConnectionData(true, "127.0.0.1", Config.Port, k_ListenAll);
            bool started = m_NetworkManager.StartHost();
            if (started) Debug.Log($"[MM] Host listening on {k_ListenAll}:{Config.Port}");
            else Debug.LogError($"[MM] Failed to start host on port {Config.Port} (port in use?)");
            return started;
        }

        public bool StartClient(string ip, ushort port)
        {
            m_Transport.SetConnectionData(true, ip, port);
            bool started = m_NetworkManager.StartClient();
            if (started) Debug.Log($"[MM] Client connecting to {ip}:{port}");
            else Debug.LogError($"[MM] Failed to start client for {ip}:{port}");
            return started;
        }

        public bool StartServer()
        {
            ApplyServerProcessSettings();
            m_Transport.SetConnectionData(true, k_ListenAll, Config.Port, k_ListenAll);

            if (m_NetworkManager.StartServer())
            {
                Debug.Log($"[MM] Server listening on {k_ListenAll}:{Config.Port}");
                return true;
            }

            Debug.LogError($"[MM] Failed to start server on {k_ListenAll}:{Config.Port} (port in use?)");
            QuitIfHeadless();
            return false;
        }

        public void Shutdown()
        {
            if (m_NetworkManager.IsListening || m_NetworkManager.IsClient)
                m_NetworkManager.Shutdown();
        }

        static void ApplyServerProcessSettings()
        {
            // A headless server has no vsync: uncapped, the main loop spins at 100% CPU and burns
            // t3 CPU credits on EC2. 30 fps matches the 30 Hz network tick rate.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 30;

            if (!Application.isEditor)
            {
                // One line per log entry; stack traces make server logs unreadable in CloudWatch.
                Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
                Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
            }
        }

        // Runs on the server for every connection request, including the host's own local client.
        void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            // ConnectedClientsIds contains the host's client but never a dedicated server,
            // so this counts players correctly in both modes.
            string reason = null;
            var match = MatchManager.Instance;
            if (m_NetworkManager.ConnectedClientsIds.Count >= GameConfig.MaxPlayers)
                reason = RejectFull;
            else if (match != null && match.IsSpawned && match.State != MatchState.WaitingForPlayers)
                reason = RejectMatchInProgress;

            response.Approved = reason == null;
            response.CreatePlayerObject = response.Approved;
            response.Reason = reason;
            response.Pending = false;

            if (reason != null)
                Debug.Log($"[MM] Rejected client {request.ClientNetworkId}: {reason}");
        }

        void OnClientConnected(ulong clientId)
        {
            if (m_NetworkManager.IsServer) Debug.Log($"[MM] Client {clientId} connected");
            else Debug.Log($"[MM] Connected to server as client {clientId}");
        }

        void OnClientDisconnected(ulong clientId)
        {
            if (m_NetworkManager.IsServer)
            {
                Debug.Log($"[MM] Client {clientId} disconnected");
                return;
            }

            string reason = m_NetworkManager.DisconnectReason;
            Debug.Log($"[MM] Disconnected from server{(string.IsNullOrEmpty(reason) ? "" : ": " + reason)}");
        }

        void OnTransportFailure()
        {
            Debug.LogError("[MM] Transport failure");
            if (m_NetworkManager.IsServer) QuitIfHeadless();
        }

        // A server that cannot serve must exit non-zero so systemd / the ASG health check notices.
        static void QuitIfHeadless()
        {
            if (Application.isEditor) return;
            if (IsDedicatedServerBuild || Config.ForceServer) Application.Quit(1);
        }
    }
}
