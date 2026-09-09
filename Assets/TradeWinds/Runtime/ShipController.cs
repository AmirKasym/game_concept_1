using UnityEngine;

namespace TradeWinds
{
    [DefaultExecutionOrder(-100)]
    public sealed class ShipController : MonoBehaviour
    {
        public ShipSimulation State { get; private set; }
        public float SeaStrength { get; set; } = 0.6f;
        public float Steering { get; set; }
        public float SailChange { get; set; }
        public bool Paused { get; set; }
        public ShipActor Helmsman { get; private set; }
        private Vector3 linearVelocity, angularVelocity;
        private bool hasPreviousPose;
        public string Notice { get; private set; } = "Подойдите к штурвалу и нажмите E.";
        [SerializeField] private Transform wheel;
        [SerializeField] private Transform sail;
        [SerializeField] private Vector3[] islands;

        private void Awake() { State = new ShipSimulation(); }

        public void Initialize(Transform wheelVisual, Transform sailVisual, Vector3[] obstacles)
        {
            State = new ShipSimulation();
            wheel = wheelVisual;
            sail = sailVisual;
            islands = obstacles;
        }

        private void FixedUpdate()
        {
            if (State == null) return;
            if (Paused) { UpdatePresentation(); Physics.SyncTransforms(); return; }
            double x = State.X, z = State.Z;
            State.Step(Time.fixedDeltaTime, Steering, SailChange);
            foreach (Vector3 island in islands)
            {
                var delta = new Vector2((float)State.X - island.x, (float)State.Z - island.z);
                if (delta.sqrMagnitude < (island.y + 11) * (island.y + 11))
                {
                    State.StopAtObstacle(x, z);
                    Notice = "Мель! Пробный выход остановлен. R — вернуться в исходную точку.";
                    break;
                }
            }
            if (State.X * State.X + State.Z * State.Z > 695 * 695)
                Notice = "Край тестового моря. Развернитесь в сторону маяка.";
            UpdatePresentation();
            Physics.SyncTransforms();
        }

        public bool TryTakeHelm(ShipActor actor)
        {
            if (Helmsman != null || actor.Interaction.HeldItem != null || !actor.NearHelm) return false;
            actor.Respawn(DeckPlayer.HelmPosition); Helmsman = actor;
            return true;
        }

        public void ReleaseHelm(ShipActor actor)
        {
            if (Helmsman != actor) return;
            Helmsman = null; Steering = SailChange = 0;
        }

        public Vector3 GetPointVelocity(Vector3 point)
        { return linearVelocity + Vector3.Cross(angularVelocity, point - transform.position); }

        public void ToggleAnchor()
        {
            State.ToggleAnchor();
            Notice = State.Anchored ? "Якорь опущен. Корабль замедляется." : "Якорь поднят. W — поставить парус.";
        }

        public void ResetVoyage()
        {
            State.Reset();
            Helmsman = null; hasPreviousPose = false;
            Steering = SailChange = 0;
            Notice = "Пробный выход начат заново.";
        }

        public void UpdatePresentation()
        {
            float t = (float)State.Clock;
            float x = (float)State.X, z = (float)State.Z;
            float heading = (float)State.Heading;
            var forward = Quaternion.Euler(0, heading, 0) * Vector3.forward;
            var right = Quaternion.Euler(0, heading, 0) * Vector3.right;
            float bow = Height(x + forward.x * 5, z + forward.z * 5, t);
            float stern = Height(x - forward.x * 5, z - forward.z * 5, t);
            float port = Height(x - right.x * 2, z - right.z * 2, t);
            float starboard = Height(x + right.x * 2, z + right.z * 2, t);
            float pitch = -Mathf.Atan2(bow - stern, 10) * Mathf.Rad2Deg;
            float roll = Mathf.Atan2(starboard - port, 4) * Mathf.Rad2Deg;
            Vector3 position = new Vector3(x, Height(x, z, t), z);
            Quaternion rotation = Quaternion.Euler(0, heading, 0) * Quaternion.Euler(pitch, 0, roll);
            if (hasPreviousPose)
            {
                linearVelocity = (position - transform.position) / Time.fixedDeltaTime;
                Quaternion delta = rotation * Quaternion.Inverse(transform.rotation);
                delta.ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 180) angle -= 360;
                angularVelocity = Mathf.Abs(angle) < 0.0001f ? Vector3.zero : axis * angle * Mathf.Deg2Rad / Time.fixedDeltaTime;
            }
            else { linearVelocity = angularVelocity = Vector3.zero; hasPreviousPose = true; }
            transform.SetPositionAndRotation(position, rotation);
            wheel.localRotation = Quaternion.Euler(0, 0, (float)-State.Rudder * 85);
            sail.localScale = new Vector3(1, Mathf.Lerp(0.12f, 1, (float)State.Sail), 1);
        }

        private float Height(float x, float z, float t)
        {
            return (float)ShipSimulation.WaveHeight(x, z, t, SeaStrength);
        }
    }
}
