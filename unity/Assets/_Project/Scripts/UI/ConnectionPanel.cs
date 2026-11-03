using System.Globalization;
using MM.Core;
using MM.Net;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MM.UI
{
    // Client-side test menu: Host / Client / Server buttons plus IP and port fields.
    // Hidden while a session runs (a Leave button replaces it); disabled entirely in dedicated server builds.
    public sealed class ConnectionPanel : MonoBehaviour
    {
        [SerializeField] NetworkBootstrap m_Bootstrap;
        [SerializeField] GameObject m_MenuRoot;
        [SerializeField] TMP_InputField m_IpInput;
        [SerializeField] TMP_InputField m_PortInput;
        [SerializeField] Button m_HostButton;
        [SerializeField] Button m_ClientButton;
        [SerializeField] Button m_ServerButton;
        [SerializeField] Button m_LeaveButton;
        [SerializeField] TMP_Text m_StatusText;

        NetworkManager m_Net;
        bool m_WasRunning;

        void Awake()
        {
            if (NetworkBootstrap.IsDedicatedServerBuild) gameObject.SetActive(false);
        }

        void Start()
        {
            m_Net = m_Bootstrap.NetworkManager;

            var config = GameConfig.Current;
            m_IpInput.text = config.ServerIp;
            m_PortInput.text = config.Port.ToString(CultureInfo.InvariantCulture);

            m_HostButton.onClick.AddListener(OnHostClicked);
            m_ClientButton.onClick.AddListener(OnClientClicked);
            m_ServerButton.onClick.AddListener(OnServerClicked);
            m_LeaveButton.onClick.AddListener(OnLeaveClicked);

            m_Net.OnClientConnectedCallback += OnClientConnected;
            m_Net.OnClientDisconnectCallback += OnClientDisconnected;

            SetStatus("");
            ApplyRunning(IsRunning);
        }

        void OnDestroy()
        {
            if (m_Net == null) return;
            m_Net.OnClientConnectedCallback -= OnClientConnected;
            m_Net.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        // Polled (three bool reads) rather than event-driven: Start/Stop events fire mid-shutdown,
        // when the role flags are not reliable yet.
        void Update()
        {
            bool running = IsRunning;
            if (running != m_WasRunning) ApplyRunning(running);
        }

        void OnHostClicked()
        {
            SetStatus(m_Bootstrap.StartHost() ? "" : "Could not start host (port in use?)");
        }

        void OnClientClicked()
        {
            string ip = m_IpInput.text.Trim();
            if (string.IsNullOrEmpty(ip) || !TryReadPort(out ushort port))
            {
                SetStatus("Enter a server IP and a port (1-65535)");
                return;
            }

            SetStatus(m_Bootstrap.StartClient(ip, port) ? $"Connecting to {ip}:{port}..." : "Could not start client");
        }

        void OnServerClicked()
        {
            SetStatus(m_Bootstrap.StartServer() ? "Server running (spectating)" : "Could not start server (port in use?)");
        }

        void OnLeaveClicked()
        {
            m_Bootstrap.Shutdown();
            SetStatus("Left the session");
        }

        bool TryReadPort(out ushort port)
        {
            return ushort.TryParse(m_PortInput.text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out port) && port > 0;
        }

        void OnClientConnected(ulong clientId)
        {
            if (!m_Net.IsServer && clientId == m_Net.LocalClientId) SetStatus("");
        }

        void OnClientDisconnected(ulong clientId)
        {
            // On a client this only fires for our own connection: rejected, kicked, timed out or server gone.
            if (m_Net.IsServer) return;
            string reason = m_Net.DisconnectReason;
            SetStatus(string.IsNullOrEmpty(reason) ? "Disconnected (server unreachable or closed)" : $"Disconnected: {reason}");
        }

        bool IsRunning => m_Net.IsListening || m_Net.IsClient || m_Net.IsServer;

        void ApplyRunning(bool running)
        {
            m_WasRunning = running;
            m_MenuRoot.SetActive(!running);
            m_LeaveButton.gameObject.SetActive(running);

            // The clicked button stays selected; clear it so Space never "submits" a UI element during a match.
            if (running && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        void SetStatus(string message) => m_StatusText.text = message;
    }
}
