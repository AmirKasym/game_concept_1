using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TradeWinds
{
    public sealed class PrototypeHud : MonoBehaviour
    {
        private ShipController ship;
        private DeckPlayer player;
        private GUIStyle title, label, muted, button, promptStyle;
        private bool lowQuality;
        private UniversalRenderPipelineAsset pipeline;
        private float originalScale, originalShadows;
        private readonly RaycastHit[] hits = new RaycastHit[32];
        private string prompt = "", heading = "000", speed = "0.0";
        private float nextTextUpdate;

        public void Initialize(ShipController controller, DeckPlayer deckPlayer)
        {
            ship = controller; player = deckPlayer;
            pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline != null) { originalScale = pipeline.renderScale; originalShadows = pipeline.shadowDistance; }
        }
        private void Update()
        {
            if (player == null || Time.unscaledTime < nextTextUpdate) return;
            nextTextUpdate = Time.unscaledTime + 0.08f;
            heading = ship.State.Heading.ToString("000"); speed = (ship.State.Speed * 1.944).ToString("0.0");
            prompt = "";
            if (player.Paused || player.ExternalView) return;
            var camera = player.View;
            Component target = PlayerInteraction.FindTarget(camera.transform.position, camera.transform.forward, hits);
            if (target is PickableItem item && !item.isCarried) prompt = "поднять " + item.ItemName;
            else if (target is LadderInteraction ladder) prompt = ladder.IsRescueRope ? "зацепиться за канат" : "встать на трап";
            else if (target is HelmInteraction && player.NearHelm) prompt = player.AtHelm ? "отпустить штурвал" : "взяться за штурвал";
        }
        private void OnGUI()
        {
            if (ship == null) return;
            if (title == null) CreateStyles();
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            Matrix4x4 original = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1280 * scale) / 2, (Screen.height - 720 * scale) / 2, 0), Quaternion.identity, Vector3.one * scale);
            if (player.Paused) DrawPause();
            else
            {
                Fill(new Rect(24, 654, 220, 42), new Color(0.02f, 0.04f, 0.045f, 0.65f));
                GUI.Label(new Rect(36, 664, 204, 24), heading + "°   |   " + speed + " уз", label);
                if (prompt.Length > 0) GUI.Label(new Rect(320, 384, 640, 54), "Нажмите [E], чтобы " + prompt, promptStyle);
            }
            GUI.matrix = original;
        }
        private void DrawPause()
        {
            Fill(new Rect(0, 0, 1280, 720), new Color(0.025f, 0.035f, 0.04f, 0.96f));
            GUI.Label(new Rect(32, 30, 400, 42), "ПАУЗА", title);
            GUI.Label(new Rect(32, 82, 800, 28), player.Session.Active ? "Сетевая игра продолжается" : "Первый рейс", muted);
            GUI.Label(new Rect(32, 144, 360, 32), "Управление", title);
            GUI.Label(new Rect(32, 194, 365, 430),
                "WASD / стрелки   Движение\nМышь   Осмотреться\nSpace   Прыжок / грести вверх\nShift   Бег / нырять\nE / F   Взаимодействие\nЛКМ   Бросить ящик\nW / S   Подъём / спуск по трапу\nSpace на трапе   Сойти\nW / S у штурвала   Парус\nA / D у штурвала   Руль\nB у штурвала   Якорь\nE у штурвала   Отпустить\nTab   Камера от третьего лица\nV   Вид корабля\nR   Начать сначала (соло)\nESC   Закрыть меню", label);
            GUI.Label(new Rect(435, 144, 420, 32), "Настройки", title);
            GUI.Label(new Rect(435, 200, 420, 24), "Чувствительность мыши", label);
            player.Sensitivity = GUI.HorizontalSlider(new Rect(435, 238, 390, 20), player.Sensitivity, 0.04f, 0.3f);
            GUI.Label(new Rect(435, 282, 420, 24), "Качка камеры", label);
            player.CameraRock = GUI.HorizontalSlider(new Rect(435, 320, 390, 20), player.CameraRock, 0, 1);
            if (GUI.Button(new Rect(435, 370, 390, 42), lowQuality ? "Графика: низкая" : "Графика: обычная", button))
            {
                lowQuality = !lowQuality; Application.targetFrameRate = lowQuality ? 30 : 60;
                if (pipeline != null) { pipeline.renderScale = lowQuality ? 0.7f : originalScale; pipeline.shadowDistance = lowQuality ? 0 : originalShadows; }
            }
            if (GUI.Button(new Rect(435, 432, 390, 42), ship.SeaStrength < 1 ? "Море: спокойное" : "Море: сильная качка", button)) ship.SeaStrength = ship.SeaStrength < 1 ? 1.6f : 0.6f;
            if (GUI.Button(new Rect(435, 620, 390, 48), "Продолжить", button)) player.SetPaused(false);
            player.Session.DrawLobbyGUI();
        }
        private void CreateStyles()
        {
            label = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true }; label.normal.textColor = Color.white;
            title = new GUIStyle(label) { fontSize = 24, fontStyle = FontStyle.Bold };
            muted = new GUIStyle(label) { fontSize = 14 }; muted.normal.textColor = new Color(0.72f, 0.77f, 0.78f);
            promptStyle = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
            button = new GUIStyle(GUI.skin.button) { fontSize = 16 };
        }
        private static void Fill(Rect rect, Color color)
        {
            Color original = GUI.color; GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = original;
        }
        private void OnDestroy()
        {
            if (pipeline != null) { pipeline.renderScale = originalScale; pipeline.shadowDistance = originalShadows; }
        }
    }
}
