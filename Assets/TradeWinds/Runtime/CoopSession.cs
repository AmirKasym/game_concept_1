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
        public float horizontal, forward, yaw, pitch;
        public bool sprint, jump, helm, anchor, cargo, throwItem, swimUp;
    }

    [Serializable]
    public struct CrewPose
    {
        public ulong id;
        public Vector3 position;
        public float yaw;
        public bool atHelm, aboard, climbing, grounded, swimming;
    }

    [Serializable]
    public sealed class VoyageSnapshot
    {
        public ShipSnapshot ship;
        public CrewPose[] crew;
        public Vector3 cargo;
        public long carrier = -1;
        public CargoPose[] items;
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
            public ShipActor actor;
            public CrewInput input;
            public float lastInput;
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
        private PickableItem[] items;
        private PickableItem[] offlineItems;
        public PickableItem[] CargoItems { get { return items; } }
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
        public string Status { get; private set; } = "E — предмет / лестница / штурвал. ЛКМ — бросить. Зелёная зона — трюм.";
        public VoyageSnapshot LastSnapshot { get; private set; }
        public ulong LocalClientId { get { return network.LocalClientId; } }

        public void Initialize(ShipController controller, DeckPlayer deckPlayer, PickableItem[] cargo, Material material)
        {
            ship = controller; player = deckPlayer; items = cargo; crewMaterial = material;
            offlineItems = (PickableItem[])items.Clone();
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
            network.NetworkConfig.ProtocolVersion = 4;
            network.NetworkConfig.TickRate = 20;
            network.AddNetworkPrefab(Resources.Load<GameObject>("NetworkCargo"));
            network.ConnectionApprovalCallback = Approve;
            network.OnClientConnectedCallback += Connected;
            network.OnClientDisconnectCallback += Disconnected;
        }

        public void StartHost()
        {
            if (Active) return;
            transport.SetConnectionData("127.0.0.1", 7777, "0.0.0.0");
            PrepareSession();
            foreach (var item in items) item.SetReplica(false);
            if (!network.StartHost()) { ReturnOffline(); Status = "Не удалось создать игру: порт 7777 занят?"; return; }
            foreach (var item in items)
            {
                item.GetComponent<CargoReplication>().PrepareSpawn();
                item.GetComponent<NetworkObject>().Spawn();
            }
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
            foreach (var item in items) item.SetReplica(true);
            foreach (var item in offlineItems) item.gameObject.SetActive(false);
            connecting = true;
            connectionDeadline = Time.unscaledTime + 12;
            if (!network.StartClient()) { connecting = false; ReturnOffline(); Status = "Не удалось подключиться."; return; }
            RegisterMessages();
            ship.Paused = true;
            Status = "Подключение к " + address + "…";
        }

        private void PrepareSession()
        {
            ClearSailors(); pending = new CrewInput(); carrier = -1; LastSnapshot = null;
            player.SetNetworkMode(true);
            ship.ResetVoyage();
            ship.Paused = false;
            cargoPosition = new Vector3(1.7f, 2.6f, -1.8f);
            ResetCargo();
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
                sailor.actor = new GameObject("Authoritative sailor " + id).AddComponent<ShipActor>();
                sailor.actor.Initialize(ship, id, new Vector3(id % 2 == 0 ? 1.5f : -1.5f, 2.2f, -5.7f + (sailors.Count / 2) * 1.1f));
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
            if (sailors.TryGetValue(id, out Sailor sailor)) Destroy(sailor.actor.gameObject);
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
            if (IsHost) foreach (var item in items)
                if (item != null && item.GetComponent<NetworkObject>().IsSpawned) item.GetComponent<NetworkObject>().Despawn(false);
            network.Shutdown();
            ReturnOffline();
            Status = "Сетевая игра закрыта. Можно создать новую.";
        }

        private void ReturnOffline()
        {
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = offlineItems[i];
                if (items[i] != null) items[i].gameObject.SetActive(true);
            }
            foreach (var avatar in avatars.Values) if (avatar != null) Destroy(avatar.gameObject);
            avatars.Clear(); ClearSailors(); carrier = -1; pending = new CrewInput();
            foreach (var item in items) item.SetReplica(false);
            player.SetNetworkMode(false);
            player.ResetPlayerAndShip();
            player.SetPaused(true);
        }

        public void SetInput(CrewInput input)
        {
            pending.horizontal = input.horizontal; pending.forward = input.forward;
            pending.yaw = input.yaw; pending.pitch = input.pitch; pending.sprint = input.sprint;
            pending.swimUp = input.swimUp;
            pending.jump |= input.jump; pending.helm |= input.helm;
            pending.anchor |= input.anchor; pending.cargo |= input.cargo;
            pending.throwItem |= input.throwItem;
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
                {
                    ShipActor actor = pair.Value.actor;
                    bool aboard = actor.Platform != null;
                    poses[index++] = new CrewPose { id = pair.Key, position = aboard ? ship.transform.InverseTransformPoint(actor.transform.position) : actor.transform.position,
                        yaw = pair.Value.input.yaw, atHelm = actor.AtHelm, aboard = aboard, climbing = actor.Climbing, grounded = actor.Grounded, swimming = actor.Swimming };
                }
                var cargoPoses = new CargoPose[items.Length];
                for (int i = 0; i < items.Length; i++) cargoPoses[i] = items[i].Capture();
                var snapshot = new VoyageSnapshot { ship = ship.State.Capture(), crew = poses, cargo = items[0].transform.position,
                    carrier = cargoPoses[0].carrier, items = cargoPoses };
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
                CrewInput input = Time.unscaledTime - sailor.lastInput > 0.35f ? new CrewInput { yaw = sailor.input.yaw, pitch = sailor.input.pitch } : sailor.input;
                sailor.actor.Step(input, Time.fixedDeltaTime);
                ClearActions(ref sailor.input);
            }
        }

        private void AcceptInput(ulong id, CrewInput input)
        {
            if (!sailors.TryGetValue(id, out Sailor sailor)) return;
            if (!Finite(input.horizontal) || !Finite(input.forward) || !Finite(input.yaw) || !Finite(input.pitch)) return;
            input.horizontal = Mathf.Clamp(input.horizontal, -1, 1);
            input.forward = Mathf.Clamp(input.forward, -1, 1);
            input.yaw = Mathf.Repeat(input.yaw, 360);
            input.pitch = Mathf.Clamp(input.pitch, -65, 70);
            input.jump |= sailor.input.jump; input.helm |= sailor.input.helm;
            input.anchor |= sailor.input.anchor; input.cargo |= sailor.input.cargo;
            input.throwItem |= sailor.input.throwItem;
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
                if (snapshot == null || snapshot.crew == null || snapshot.crew.Length > 4 || snapshot.items == null || snapshot.items.Length != items.Length) return;
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
            // Cargo motion and attachment state are owned by NGO components, not this snapshot.
            visibleIds.Clear();
            foreach (CrewPose pose in snapshot.crew)
            {
                visibleIds.Add(pose.id);
                if (pose.id == network.LocalClientId) player.ApplyNetworkPose(pose);
                if (!avatars.TryGetValue(pose.id, out SailorAvatar avatar))
                {
                    avatar = new GameObject().AddComponent<SailorAvatar>();
                    avatar.name = "Матрос " + (pose.id + 1);
                    avatar.transform.SetParent(ship.transform, false);
                    avatar.Build(crewMaterial, (int)(pose.id % 4));
                    avatars[pose.id] = avatar;
                }
                Transform parent = pose.aboard ? ship.transform : null;
                if (avatar.transform.parent != parent) avatar.transform.SetParent(parent, true);
                bool holdsCargo = false;
                foreach (CargoPose cargo in snapshot.items) holdsCargo |= cargo.carrier == (long)pose.id;
                avatar.SetPose(pose.position, pose.yaw - (pose.aboard ? (float)ship.State.Heading : 0), pose.id == network.LocalClientId && !player.ExternalView, holdsCargo);
            }
            removedIds.Clear();
            foreach (var pair in avatars) if (!visibleIds.Contains(pair.Key)) removedIds.Add(pair.Key);
            foreach (ulong id in removedIds) { Destroy(avatars[id].gameObject); avatars.Remove(id); }
        }

        public void ResetCargo() { foreach (var item in items) item.ResetItem(); }

        public void RegisterCargo(int id, PickableItem item)
        {
            if (id < 0 || id >= items.Length) throw new InvalidOperationException("Invalid network cargo id.");
            var source = offlineItems[id];
            Vector3 position = item.transform.position; Quaternion rotation = item.transform.rotation;
            item.Configure(id, ship, source.ItemName, source.Weight);
            item.Body.position = position; item.Body.rotation = rotation;
            items[id] = item;
        }

        private void ClearSailors()
        {
            foreach (var sailor in sailors.Values) if (sailor.actor != null) Destroy(sailor.actor.gameObject);
            sailors.Clear();
        }

        private void LateUpdate()
        {
            offlineAvatar.gameObject.SetActive(!Active);
            if (!Active)
            {
                Transform parent = player.Aboard ? ship.transform : null;
                if (offlineAvatar.transform.parent != parent) offlineAvatar.transform.SetParent(parent, true);
                offlineAvatar.SetPose(player.Aboard ? player.DeckPosition : player.WorldPosition,
                    player.LookYaw - (player.Aboard ? (float)ship.State.Heading : 0), !player.ExternalView, player.OfflineActor.Interaction.HeldItem != null);
            }
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
            GUI.Label(new Rect(932, 408, 300, 110), Status);
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static void ClearActions(ref CrewInput input) { input.jump = input.helm = input.anchor = input.cargo = input.throwItem = false; }

        private void OnDestroy()
        {
            if (network == null) return;
            network.OnClientConnectedCallback -= Connected;
            network.OnClientDisconnectCallback -= Disconnected;
            ClearSailors();
            network.Shutdown();
            Destroy(network.gameObject);
        }
    }
}
