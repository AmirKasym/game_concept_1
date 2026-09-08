using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace TradeWinds
{
    [Serializable]
    public struct CrewInput
    {
        public float horizontal, forward, yaw;
        public bool sprint, jump, helm, anchor, cargo;
    }

    [Serializable]
    public struct CrewPose
    {
        public ulong id;
        public Vector3 position;
        public float yaw;
        public bool atHelm;
    }

    [Serializable]
    public sealed class VoyageSnapshot
    {
        public ShipSnapshot ship;
        public CrewPose[] crew;
        public Vector3 cargo;
        public long carrier = -1;
    }

    // Small LAN prototype: NGO supplies connections and reliable delivery; only the
    // host steps gameplay. Named snapshots avoid parenting NetworkTransforms to a
    // rocking, procedurally generated ship. No client submits a position or ship state.
    public sealed class CoopSession : MonoBehaviour
    {
        private const string InputMessage = "voyage/input-v1", StateMessage = "voyage/state-v1";
        private const float SendInterval = 0.05f;
        private sealed class Sailor
        {
            public readonly DeckMotor motor = new DeckMotor();
            public CrewInput input;
            public float lastInput;
            public bool atHelm;
        }
        private readonly Dictionary<ulong, Sailor> sailors = new Dictionary<ulong, Sailor>();
        private readonly Dictionary<ulong, SailorAvatar> avatars = new Dictionary<ulong, SailorAvatar>();
        private SailorAvatar offlineAvatar;
        private readonly HashSet<ulong> visibleIds = new HashSet<ulong>();
        private readonly List<ulong> removedIds = new List<ulong>();
        private NetworkManager network;
        private UnityTransport transport;
        private ShipController ship;
        private DeckPlayer player;
        private Transform crate;
        private Material crewMaterial;
        private CrewInput pending;
        private float nextSend;
        private float connectionDeadline;
        private bool connecting;
        private string address = "127.0.0.1";
        private Vector3 cargoPosition = new Vector3(1.7f, 2.6f, -1.8f);
        private long carrier = -1;
        public bool Active { get { return connecting || (network != null && network.IsListening); } }
        public bool IsHost { get { return network != null && network.IsHost; } }
        public int CrewCount { get { return IsHost ? sailors.Count : avatars.Count; } }
        public string Status { get; private set; } = "F — поднять ящик рядом. ESC — меню кооператива.";
        public VoyageSnapshot LastSnapshot { get; private set; }
        public ulong LocalClientId { get { return network.LocalClientId; } }

        public void Initialize(ShipController controller, DeckPlayer deckPlayer, Transform cargo, Material material)
        {
            ship = controller; player = deckPlayer; crate = cargo; crewMaterial = material;
            Application.runInBackground = true;
            player.Session = this;
            offlineAvatar = new GameObject("Матрос · одиночная игра").AddComponent<SailorAvatar>();
            offlineAvatar.transform.SetParent(ship.transform, false);
            offlineAvatar.Build(crewMaterial, 0);
            var root = new GameObject("Coop connection");
            transport = root.AddComponent<UnityTransport>();
            network = root.AddComponent<NetworkManager>();
            network.NetworkConfig = new NetworkConfig();
            network.NetworkConfig.NetworkTransport = transport;
            network.NetworkConfig.EnableSceneManagement = false;
            network.NetworkConfig.ConnectionApproval = true;
            network.NetworkConfig.ProtocolVersion = 2;
            network.NetworkConfig.TickRate = 20;
            network.ConnectionApprovalCallback = Approve;
            network.OnClientConnectedCallback += Connected;
            network.OnClientDisconnectCallback += Disconnected;
        }

        public void StartHost()
        {
            if (Active) return;
            transport.SetConnectionData("127.0.0.1", 7777, "0.0.0.0");
            PrepareSession();
            if (!network.StartHost()) { Status = "Не удалось создать игру: порт 7777 занят?"; return; }
            RegisterMessages();
            Status = "Хост открыт • порт 7777 • до 4 игроков";
            player.SetPaused(false);
        }

        public void StartClient(string hostAddress = null)
        {
            if (Active) return;
            if (hostAddress != null) address = hostAddress;
            if (string.IsNullOrWhiteSpace(address)) { Status = "Введите адрес хоста."; return; }
            transport.SetConnectionData(address.Trim(), 7777);
            PrepareSession();
            connecting = true;
            connectionDeadline = Time.unscaledTime + 12;
            if (!network.StartClient()) { connecting = false; Status = "Не удалось подключиться."; return; }
            RegisterMessages();
            ship.Paused = true;
            Status = "Подключение к " + address + "…";
        }

        private void PrepareSession()
        {
            sailors.Clear(); pending = new CrewInput(); carrier = -1;
            ship.ResetVoyage();
            ship.Paused = false;
            cargoPosition = new Vector3(1.7f, 2.6f, -1.8f);
            nextSend = 0;
        }

        private void RegisterMessages()
        {
            network.CustomMessagingManager.RegisterNamedMessageHandler(InputMessage, ReceiveInput);
            network.CustomMessagingManager.RegisterNamedMessageHandler(StateMessage, ReceiveState);
        }

        private void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.Approved = network.ConnectedClientsIds.Count < 4;
            response.CreatePlayerObject = false;
            response.Reason = response.Approved ? "" : "Экипаж уже заполнен (4 игрока).";
            response.Pending = false;
        }

        private void Connected(ulong id)
        {
            if (IsHost)
            {
                var sailor = new Sailor();
                sailor.motor.Reset(id % 2 == 0 ? 1.5f : -1.5f, -5.7f + (sailors.Count / 2) * 1.1f);
                sailors[id] = sailor;
            }
            if (id == network.LocalClientId)
            {
                connecting = false;
                Status = IsHost ? "Хост открыт • до 4 игроков" : "Вы в экипаже • кораблём управляет хост";
                player.SetPaused(false);
            }
        }

        private void Disconnected(ulong id)
        {
            sailors.Remove(id);
            if (carrier == (long)id) carrier = -1;
            if (!IsHost)
            {
                connecting = false;
                Status = "Соединение закрыто. " + network.DisconnectReason;
                ReturnOffline();
            }
        }

        public void StopSession()
        {
            connecting = false;
            network.Shutdown();
            ReturnOffline();
            Status = "Сетевая игра закрыта. Можно создать новую.";
        }

        private void ReturnOffline()
        {
            foreach (var avatar in avatars.Values) if (avatar != null) Destroy(avatar.gameObject);
            avatars.Clear(); sailors.Clear(); carrier = -1; pending = new CrewInput();
            player.ResetPlayerAndShip();
            player.SetPaused(true);
        }

        public void SetInput(CrewInput input)
        {
            pending.horizontal = input.horizontal; pending.forward = input.forward;
            pending.yaw = input.yaw; pending.sprint = input.sprint;
            pending.jump |= input.jump; pending.helm |= input.helm;
            pending.anchor |= input.anchor; pending.cargo |= input.cargo;
        }

        private void Update()
        {
            if (connecting && Time.unscaledTime > connectionDeadline)
            {
                StopSession(); Status = "Хост не ответил. Проверьте адрес и что игра создана.";
            }
            if (!Active || Time.unscaledTime < nextSend) return;
            nextSend = Time.unscaledTime + SendInterval;
            if (IsHost)
            {
                AcceptInput(network.LocalClientId, pending);
                ClearActions(ref pending);
                var poses = new CrewPose[sailors.Count];
                int index = 0;
                foreach (var pair in sailors)
                    poses[index++] = new CrewPose { id = pair.Key, position = Position(pair.Value.motor),
                        yaw = pair.Value.input.yaw, atHelm = pair.Value.atHelm };
                var snapshot = new VoyageSnapshot { ship = ship.State.Capture(), crew = poses, cargo = cargoPosition, carrier = carrier };
                Present(snapshot);
                foreach (ulong id in network.ConnectedClientsIds)
                    if (id != network.LocalClientId) Send(StateMessage, id, snapshot);
            }
            else if (network.IsConnectedClient)
            {
                Send(InputMessage, NetworkManager.ServerClientId, pending);
                ClearActions(ref pending);
            }
        }

        private void FixedUpdate()
        {
            if (!IsHost) return;
            ship.Steering = ship.SailChange = 0;
            foreach (var pair in sailors)
            {
                Sailor sailor = pair.Value;
                CrewInput input = Time.unscaledTime - sailor.lastInput > 0.35f ? new CrewInput { yaw = sailor.input.yaw } : sailor.input;
                if (input.helm && sailor.motor.Grounded)
                {
                    bool occupied = false;
                    foreach (var other in sailors.Values) occupied |= other != sailor && other.atHelm;
                    if (sailor.atHelm) sailor.atHelm = false;
                    else if (!occupied && Vector3.Distance(Position(sailor.motor), DeckPlayer.HelmPosition) < 2.4f && carrier != (long)pair.Key)
                    { sailor.atHelm = true; sailor.motor.Reset(0, -5.3f); }
                }
                if (input.jump) sailor.atHelm = false;
                if (sailor.atHelm)
                {
                    ship.Steering = input.horizontal; ship.SailChange = input.forward;
                    if (input.anchor) ship.ToggleAnchor();
                }
                else sailor.motor.Step(Time.fixedDeltaTime, input.horizontal, input.forward, input.yaw,
                    input.sprint && carrier != (long)pair.Key, input.jump);
                if (input.cargo && !sailor.atHelm) Interact(pair.Key, Position(sailor.motor));
                ClearActions(ref sailor.input);
                if (carrier == (long)pair.Key) cargoPosition = Position(sailor.motor) + Vector3.up * 0.9f
                    + Quaternion.Euler(0, input.yaw, 0) * Vector3.forward * 0.85f;
            }
        }

        private void AcceptInput(ulong id, CrewInput input)
        {
            if (!sailors.TryGetValue(id, out Sailor sailor)) return;
            if (!Finite(input.horizontal) || !Finite(input.forward) || !Finite(input.yaw)) return;
            input.horizontal = Mathf.Clamp(input.horizontal, -1, 1);
            input.forward = Mathf.Clamp(input.forward, -1, 1);
            input.yaw = Mathf.Repeat(input.yaw, 360);
            input.jump |= sailor.input.jump; input.helm |= sailor.input.helm;
            input.anchor |= sailor.input.anchor; input.cargo |= sailor.input.cargo;
            sailor.input = input; sailor.lastInput = Time.unscaledTime;
        }

        private void ReceiveInput(ulong sender, FastBufferReader reader)
        {
            if (!IsHost || reader.Length > 2048) return;
            try { reader.ReadValueSafe(out string json); AcceptInput(sender, JsonUtility.FromJson<CrewInput>(json)); }
            catch (Exception) { Debug.LogWarning("Rejected malformed crew input."); }
        }

        private void ReceiveState(ulong sender, FastBufferReader reader)
        {
            if (IsHost || sender != NetworkManager.ServerClientId || reader.Length > 8192) return;
            try
            {
                reader.ReadValueSafe(out string json);
                var snapshot = JsonUtility.FromJson<VoyageSnapshot>(json);
                if (snapshot == null || snapshot.crew == null || snapshot.crew.Length > 4) return;
                ship.State.Restore(snapshot.ship);
                Present(snapshot);
            }
            catch (Exception) { Debug.LogWarning("Rejected malformed voyage snapshot."); }
        }

        private void Send<T>(string message, ulong target, T payload)
        {
            string json = JsonUtility.ToJson(payload);
            using (var writer = new FastBufferWriter(8192, Allocator.Temp))
            {
                writer.WriteValueSafe(json);
                network.CustomMessagingManager.SendNamedMessage(message, target, writer, NetworkDelivery.ReliableFragmentedSequenced);
            }
        }

        private void Present(VoyageSnapshot snapshot)
        {
            LastSnapshot = snapshot;
            cargoPosition = snapshot.cargo; carrier = snapshot.carrier;
            visibleIds.Clear();
            foreach (CrewPose pose in snapshot.crew)
            {
                visibleIds.Add(pose.id);
                if (pose.id == network.LocalClientId) player.ApplyNetworkPose(pose.position, pose.atHelm);
                if (!avatars.TryGetValue(pose.id, out SailorAvatar avatar))
                {
                    avatar = new GameObject().AddComponent<SailorAvatar>();
                    avatar.name = "Матрос " + (pose.id + 1);
                    avatar.transform.SetParent(ship.transform, false);
                    avatar.Build(crewMaterial, (int)(pose.id % 4));
                    avatars[pose.id] = avatar;
                }
                avatar.SetPose(pose.position, pose.yaw, pose.id == network.LocalClientId && !player.ExternalView, carrier == (long)pose.id);
            }
            removedIds.Clear();
            foreach (var pair in avatars) if (!visibleIds.Contains(pair.Key)) removedIds.Add(pair.Key);
            foreach (ulong id in removedIds) { Destroy(avatars[id].gameObject); avatars.Remove(id); }
        }

        public void InteractOffline()
        {
            if (!Active && !player.AtHelm) Interact(0, player.DeckPosition);
        }

        private void Interact(ulong id, Vector3 position)
        {
            if (carrier == (long)id)
            {
                // Drop at the sailor's known valid feet position, never through the rail or into fixed cargo.
                cargoPosition = position + Vector3.up * 0.45f;
                carrier = -1; Status = "Ящик поставлен на палубу.";
            }
            else if (carrier == -1 && Vector3.Distance(position + Vector3.up * 0.5f, cargoPosition) < 1.8f)
            { carrier = (long)id; Status = "Ящик в руках • F — поставить"; }
        }

        private void LateUpdate()
        {
            offlineAvatar.gameObject.SetActive(!Active);
            if (!Active) offlineAvatar.SetPose(player.DeckPosition, player.LookYaw, !player.ExternalView, carrier == 0);
            if (!Active && carrier == 0)
                cargoPosition = player.DeckPosition + Vector3.up * 0.9f
                    + Quaternion.Euler(0, player.LookYaw, 0) * Vector3.forward * 0.85f;
            crate.localPosition = cargoPosition;
        }

        public void DrawLobbyGUI()
        {
            GUI.Box(new Rect(915, 145, 337, 310), "ЭТАП 2 • ЭКИПАЖ");
            GUI.Label(new Rect(932, 180, 300, 45), Active ? "В игре: " + CrewCount + " / 4" : "Локальная сеть или два окна на одном ПК");
            if (!Active)
            {
                GUI.Label(new Rect(932, 232, 300, 24), "Адрес компьютера хоста:");
                address = GUI.TextField(new Rect(932, 260, 300, 30), address, 128);
                if (GUI.Button(new Rect(932, 305, 300, 40), "СОЗДАТЬ ИГРУ")) StartHost();
                if (GUI.Button(new Rect(932, 355, 300, 40), "ПОДКЛЮЧИТЬСЯ")) StartClient();
            }
            else if (GUI.Button(new Rect(932, 260, 300, 45), "ОТКЛЮЧИТЬСЯ")) StopSession();
            GUI.Label(new Rect(932, 408, 300, 40), "Порт 7777 • Steam-лобби ещё нет");
        }

        private static Vector3 Position(DeckMotor motor) { return new Vector3(motor.X, motor.Y, motor.Z); }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static void ClearActions(ref CrewInput input) { input.jump = input.helm = input.anchor = input.cargo = false; }

        private void OnDestroy()
        {
            if (network == null) return;
            network.OnClientConnectedCallback -= Connected;
            network.OnClientDisconnectCallback -= Disconnected;
            network.Shutdown();
            Destroy(network.gameObject);
        }
    }
}
