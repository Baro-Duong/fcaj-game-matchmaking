using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MM.Gameplay
{
    // On the Player prefab (one per connected player).
    // Owner client: reads Space and sends PressRpc. Server: owns slot + score (MatchManager decides).
    // Every peer: places the square left/right and colours it from the synced slot.
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class PlayerPresser : NetworkBehaviour
    {
        public const int NoSlot = -1;

        static readonly Color[] k_SlotColors =
        {
            new Color(0.25f, 0.6f, 1f),   // P1, left
            new Color(1f, 0.45f, 0.3f)    // P2, right
        };

        // Registry instead of FindObjectsByType so the HUD can look players up every frame for free.
        static readonly List<PlayerPresser> s_All = new List<PlayerPresser>();
        public static IReadOnlyList<PlayerPresser> All => s_All;

        [SerializeField] SpriteRenderer m_Renderer;
        [SerializeField] float m_SlotX = 3f;

        // Server writes, everyone reads (NetworkVariable default permissions).
        readonly NetworkVariable<int> m_Presses = new NetworkVariable<int>();
        readonly NetworkVariable<int> m_Slot = new NetworkVariable<int>(NoSlot);

        public int Presses => m_Presses.Value;
        public int Slot => m_Slot.Value;

        public static Color ColorOf(int slot) =>
            slot >= 0 && slot < k_SlotColors.Length ? k_SlotColors[slot] : Color.gray;

        // Static lists survive Play Mode when domain reload is disabled; start clean each session.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_All.Clear();

        void Awake()
        {
            if (m_Renderer == null) m_Renderer = GetComponent<SpriteRenderer>();
        }

        public override void OnNetworkSpawn()
        {
            s_All.Add(this);
            m_Slot.OnValueChanged += OnSlotChanged;
            ApplySlotVisuals(m_Slot.Value);

            if (IsServer)
            {
                var match = MatchManager.Instance;
                if (match != null) match.ServerRegisterPlayer(this);
            }
        }

        public override void OnNetworkDespawn()
        {
            s_All.Remove(this);
            m_Slot.OnValueChanged -= OnSlotChanged;

            if (IsServer)
            {
                var match = MatchManager.Instance;
                if (match != null) match.ServerUnregisterPlayer(this);
            }
        }

        void Update()
        {
            if (!IsOwner || !IsSpawned) return;

            var keyboard = Keyboard.current;   // null on a headless server or with no keyboard attached
            if (keyboard == null || !keyboard.spaceKey.wasPressedThisFrame) return;

            // Saves bandwidth only; the server re-checks state and rate itself.
            var match = MatchManager.Instance;
            if (match != null && match.State != MatchState.Playing) return;

            PressRpc();
        }

        // Client -> server: "I pressed". The client never sends a score.
        [Rpc(SendTo.Server)]
        void PressRpc(RpcParams rpcParams = default)
        {
            // Universal RPCs do not require ownership by default: reject presses sent for someone else's square.
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;

            var match = MatchManager.Instance;
            if (match != null) match.ServerTryRegisterPress(this);
        }

        internal void ServerAssignSlot(int slot) => m_Slot.Value = slot;

        internal void ServerSetPresses(int presses) => m_Presses.Value = presses;

        void OnSlotChanged(int previous, int current) => ApplySlotVisuals(current);

        void ApplySlotVisuals(int slot)
        {
            bool hasSlot = slot == 0 || slot == 1;
            m_Renderer.enabled = hasSlot;
            if (!hasSlot) return;

            // No NetworkTransform needed: the position is a pure function of the synced slot.
            transform.position = new Vector3(slot == 0 ? -m_SlotX : m_SlotX, 0f, 0f);
            m_Renderer.color = ColorOf(slot);
        }
    }
}
