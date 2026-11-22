using UnityEngine;
using UnityEngine.UIElements;

namespace BackRooms.UI
{
    /// <summary>
    /// Peças de interface (UI Toolkit) comuns ao menu inicial (<see cref="ConnectionMenu"/>) e ao menu
    /// de pausa (<see cref="PauseMenu"/>): as cores, o texto, o botão e a tela de opções
    /// (<see cref="GameOptions"/>), feita num lugar só.
    /// </summary>
    public static class MenuKit
    {
        public static readonly Color Yellow = new Color(0.87f, 0.78f, 0.36f);
        public static readonly Color Paper = new Color(0.93f, 0.9f, 0.78f);
        public static readonly Color PanelColor = new Color(0.04f, 0.035f, 0.02f, 0.82f);
        public static readonly Color ButtonColor = new Color(0.33f, 0.29f, 0.12f);
        public static readonly Color CurrentColor = new Color(0.55f, 0.47f, 0.16f);
        public static readonly Color LockedColor = new Color(0.16f, 0.15f, 0.12f);
        public static readonly Color Green = new Color(0.45f, 0.85f, 0.45f);

        const string OptionsText = "Options";
        const string VolumeText = "Volume";
        const string MouseText = "Mouse";
        const string QualityText = "Quality";

        public static Label Text(string text, int size, Color color)
        {
            var label = new Label(text);
            label.style.fontSize = size;
            label.style.color = color;
            return label;
        }

        public static Button MenuButton(string text, System.Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.style.height = 48;
            button.style.fontSize = 19;
            button.style.color = Paper;
            button.style.backgroundColor = ButtonColor;
            button.style.borderTopWidth = 0; button.style.borderBottomWidth = 0;
            button.style.borderLeftWidth = 0; button.style.borderRightWidth = 0;
            button.style.marginLeft = 0; button.style.marginRight = 0;
            return button;
        }

        /// <summary>Cantos arredondados de painel.</summary>
        public static void Round(VisualElement element, float radius = 6f)
        {
            element.style.borderTopLeftRadius = radius; element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius; element.style.borderBottomRightRadius = radius;
        }

        /// <summary>
        /// Opções desta máquina: um botão "Opções" que abre e fecha três linhas (volume, mouse,
        /// qualidade), cada uma com - e +. Põe as duas coisas em <paramref name="parent"/> e devolve
        /// o painel das linhas (escondido no começo).
        /// </summary>
        public static OptionsPanel AddOptions(VisualElement parent)
        {
            var panel = new OptionsPanel();
            Button toggle = MenuButton(OptionsText, () => panel.Toggle());
            toggle.style.marginTop = 14;
            toggle.style.height = 36;
            toggle.style.fontSize = 16;
            parent.Add(toggle);
            panel.ToggleButton = toggle;

            panel.Root = new VisualElement();
            panel.Root.style.display = DisplayStyle.None;
            panel.Root.style.marginTop = 6;
            parent.Add(panel.Root);

            panel.Volume = panel.Row(VolumeText, () => GameOptions.SetVolume(GameOptions.Volume - 0.1f), () => GameOptions.SetVolume(GameOptions.Volume + 0.1f));
            panel.Mouse = panel.Row(MouseText, () => GameOptions.SetMouseScale(GameOptions.MouseScale - 0.25f), () => GameOptions.SetMouseScale(GameOptions.MouseScale + 0.25f));
            panel.Quality = panel.Row(QualityText, () => GameOptions.SetQuality(GameOptions.Quality - 1), () => GameOptions.SetQuality(GameOptions.Quality + 1));
            return panel;
        }

        /// <summary>As três linhas de opções e o botão que as abre.</summary>
        public sealed class OptionsPanel
        {
            public Button ToggleButton;
            public VisualElement Root;
            public Label Volume, Mouse, Quality;

            public bool Open => Root.style.display == DisplayStyle.Flex;

            public void Toggle() => SetOpen(!Open);

            public void SetOpen(bool open)
            {
                Root.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                Refresh();
            }

            public void Refresh()
            {
                if (Volume == null) return;
                Volume.text = Mathf.RoundToInt(GameOptions.Volume * 100f) + "%";
                Mouse.text = GameOptions.MouseScale.ToString("0.00") + "x";
                Quality.text = GameOptions.QualityName;
            }

            internal Label Row(string name, System.Action less, System.Action more)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 4;
                var label = Text(name, 15, Paper);
                label.style.width = 120;
                row.Add(label);
                Button minus = MenuButton("-", () => { less(); Refresh(); });
                minus.style.width = 40; minus.style.height = 30;
                row.Add(minus);
                var value = Text("", 15, Yellow);
                value.style.width = 150;
                value.style.unityTextAlign = TextAnchor.MiddleCenter;
                row.Add(value);
                Button plus = MenuButton("+", () => { more(); Refresh(); });
                plus.style.width = 40; plus.style.height = 30;
                row.Add(plus);
                Root.Add(row);
                return value;
            }
        }
    }
}
