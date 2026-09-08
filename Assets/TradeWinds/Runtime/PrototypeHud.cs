using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TradeWinds
{
    public sealed class PrototypeHud : MonoBehaviour
    {
        private ShipController ship;
        private DeckPlayer player;
        private GUIStyle title, label, muted, number, button;
        private readonly Color ink = new Color(0.035f, 0.09f, 0.11f, 0.93f);
        private readonly Color gold = new Color(0.88f, 0.71f, 0.42f);
        private bool lowQuality;
        private UniversalRenderPipelineAsset pipeline;
        private float originalScale;
        private float originalShadows;
        private bool visitedHelm, sailed, walked;
        private string heading = "000°", speed = "0.0", distance = "0 м";
        private float nextTextUpdate;

        public void Initialize(ShipController controller, DeckPlayer deckPlayer)
        {
            ship = controller;
            player = deckPlayer;
            pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline != null) { originalScale = pipeline.renderScale; originalShadows = pipeline.shadowDistance; }
        }

        private void Update()
        {
            visitedHelm |= player.AtHelm;
            sailed |= ship.State.Distance > 100;
            walked |= visitedHelm && !player.AtHelm && !player.NearHelm;
            if (Time.unscaledTime < nextTextUpdate) return;
            nextTextUpdate = Time.unscaledTime + 0.15f;
            heading = ship.State.Heading.ToString("000") + "°";
            speed = (ship.State.Speed * 1.944).ToString("0.0");
            distance = ship.State.Distance.ToString("0") + " м";
        }

        private void OnGUI()
        {
            if (ship == null) return;
            if (title == null) CreateStyles();
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            Matrix4x4 original = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1280 * scale) / 2, (Screen.height - 720 * scale) / 2, 0), Quaternion.identity, Vector3.one * scale);

            Panel(new Rect(28, 28, 320, 86));
            GUI.Label(new Rect(48, 40, 290, 18), "ПЕРВЫЙ РЕЙС   /   ПРОТОТИП 01", muted);
            GUI.Label(new Rect(48, 62, 285, 42), "ПОПУТНЫЙ ВЕТЕР", title);
            Panel(new Rect(520, 28, 240, 76));
            GUI.Label(new Rect(541, 41, 70, 22), "КУРС", muted);
            GUI.Label(new Rect(626, 38, 115, 42), heading, number);
            Panel(new Rect(990, 28, 262, 76));
            GUI.Label(new Rect(1010, 40, 230, 22), "«КУНИЦА»  •  1 МАТРОС", label);
            GUI.Label(new Rect(1010, 66, 230, 22), ship.State.Anchored ? "НА ЯКОРЕ" : "ПОД ПАРУСОМ", muted);

            Panel(new Rect(28, 140, 286, 176));
            GUI.Label(new Rect(48, 156, 240, 24), "ПРОБНЫЙ ВЫХОД", label);
            Check(48, 196, visitedHelm, "Встать за штурвал");
            Check(48, 230, sailed, "Пройти 100 метров");
            Check(48, 264, walked, "Пройтись по палубе на ходу");

            Panel(new Rect(28, 534, 286, 158));
            GUI.Label(new Rect(48, 548, 230, 24), player.AtHelm ? "КАПИТАН" : "НА ПАЛУБЕ", muted);
            GUI.Label(new Rect(48, 576, 120, 45), speed, number);
            GUI.Label(new Rect(156, 590, 135, 25), "узлов  /  " + distance, muted);
            GUI.Label(new Rect(48, 630, 230, 20), "ПАРУС   " + Mathf.RoundToInt((float)ship.State.Sail * 100) + "%", label);
            Fill(new Rect(48, 660, 244, 4), new Color(0.23f, 0.32f, 0.33f));
            Fill(new Rect(48, 660, 244 * (float)ship.State.Sail, 4), gold);

            Panel(new Rect(344, 602, 908, 90));
            string prompt = player.AtHelm ? "W / S  Парус     A / D  Руль     ПРОБЕЛ  Якорь     E  Отпустить штурвал"
                : player.NearHelm ? "E  Взяться за штурвал     WASD  Ходить     МЫШЬ  Смотреть" : "WASD  Ходить     SHIFT  Быстрее     МЫШЬ  Смотреть";
            GUI.Label(new Rect(364, 614, 862, 25), prompt, label);
            GUI.Label(new Rect(364, 649, 860, 25), "TAB  Вид снаружи     ESC  Пауза и настройки     R  Сначала", muted);
            if (!player.ExternalView && !player.Paused) GUI.Label(new Rect(634, 350, 20, 24), "+", label);
            GUI.Label(new Rect(354, 550, 880, 42), ship.Notice, label);
            if (player.Paused) DrawPause();
            GUI.matrix = original;
        }

        private void DrawPause()
        {
            Fill(new Rect(0, 0, 1280, 720), new Color(0.02f, 0.055f, 0.07f, 0.85f));
            Panel(new Rect(390, 100, 500, 520));
            GUI.Label(new Rect(426, 130, 440, 42), "ТИХАЯ ГАВАНЬ", title);
            GUI.Label(new Rect(426, 179, 425, 40), "Пауза. Корабль и море остановлены.", muted);
            GUI.Label(new Rect(426, 231, 420, 24), "Чувствительность мыши", label);
            player.Sensitivity = GUI.HorizontalSlider(new Rect(426, 267, 420, 20), player.Sensitivity, 0.04f, 0.3f);
            GUI.Label(new Rect(426, 300, 420, 24), "Качка камеры", label);
            player.CameraRock = GUI.HorizontalSlider(new Rect(426, 336, 420, 20), player.CameraRock, 0, 1);
            if (GUI.Button(new Rect(426, 377, 420, 40), lowQuality ? "Графика: низкая · 30 FPS" : "Графика: обычная · 60 FPS", button))
            {
                lowQuality = !lowQuality;
                Application.targetFrameRate = lowQuality ? 30 : 60;
                if (pipeline != null) { pipeline.renderScale = lowQuality ? 0.7f : originalScale; pipeline.shadowDistance = lowQuality ? 0 : originalShadows; }
            }
            if (GUI.Button(new Rect(426, 429, 420, 40), ship.SeaStrength < 1 ? "Море: спокойное" : "Море: сильная качка (тест)", button))
                ship.SeaStrength = ship.SeaStrength < 1 ? 1.6f : 0.6f;
            if (GUI.Button(new Rect(426, 495, 420, 50), "ВЕРНУТЬСЯ НА ПАЛУБУ", button)) player.SetPaused(false);
            GUI.Label(new Rect(426, 568, 430, 24), "Одиночная сцена • торговля и кооп — следующие этапы", muted);
        }

        private void CreateStyles()
        {
            title = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold };
            title.normal.textColor = gold;
            label = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true };
            label.normal.textColor = new Color(0.9f, 0.91f, 0.83f);
            muted = new GUIStyle(label) { fontSize = 13 };
            muted.normal.textColor = new Color(0.59f, 0.71f, 0.71f);
            number = new GUIStyle(label) { fontSize = 34, fontStyle = FontStyle.Bold };
            button = new GUIStyle(GUI.skin.button) { fontSize = 16 };
        }

        private void Check(float x, float y, bool done, string text)
        {
            GUI.Label(new Rect(x, y, 250, 30), (done ? "✓  " : "—  ") + text, done ? muted : label);
        }
        private void Panel(Rect rect) { Fill(rect, ink); Fill(new Rect(rect.x, rect.y, 2, rect.height), gold); }
        private static void Fill(Rect rect, Color color)
        {
            Color original = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = original;
        }
        private void OnDestroy()
        {
            if (pipeline != null) { pipeline.renderScale = originalScale; pipeline.shadowDistance = originalShadows; }
        }
    }
}
