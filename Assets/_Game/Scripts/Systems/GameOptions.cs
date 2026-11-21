using UnityEngine;

namespace BackRooms
{
    /// <summary>
    /// As opções desta máquina, guardadas em <c>PlayerPrefs</c> e aplicadas ao abrir o jogo: volume
    /// geral, sensibilidade do mouse (multiplica a do <c>PlayerController</c>) e qualidade (os três
    /// perfis do HDRP em ProjectSettings/QualitySettings: High Fidelity, Balanced, Performant).
    /// A tela fica no menu (<c>ConnectionMenu</c>, botão Opções).
    /// </summary>
    public static class GameOptions
    {
        public const string VolumeKey = "BackRooms.Options.Volume";
        public const string MouseKey = "BackRooms.Options.Mouse";
        public const string QualityKey = "BackRooms.Options.Quality";

        /// <summary>Volume geral, de 0 a 1.</summary>
        public static float Volume { get; private set; } = 1f;

        /// <summary>Multiplicador da sensibilidade do mouse, de 0,25 a 3.</summary>
        public static float MouseScale { get; private set; } = 1f;

        /// <summary>
        /// Silêncio do menu: com a sessão fora do jogo (menu inicial, conectando, fim da fita) só a
        /// música do menu se ouve. A fase atrás do menu é montada (<c>LevelBuilder.buildOnAwake</c>) e
        /// os sons dela (zumbido dos pontos do objetivo, ar-condicionado, maré, inimigos) nascem
        /// tocando; em vez de cada um saber do menu, o ouvinte fica mudo e a <c>MenuMusic</c> passa
        /// por cima (<c>AudioSource.ignoreListenerVolume</c>, com o volume geral aplicado por ela).
        /// Ligado e desligado pelo <c>ConnectionMenu</c>.
        /// </summary>
        public static bool MenuSilence { get; private set; }

        /// <summary>Perfil de qualidade (índice em QualitySettings.names).</summary>
        public static int Quality => QualitySettings.GetQualityLevel();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Load()
        {
            MenuSilence = false;
            SetVolume(PlayerPrefs.GetFloat(VolumeKey, 1f), false);
            SetMouseScale(PlayerPrefs.GetFloat(MouseKey, 1f), false);
            int quality = PlayerPrefs.GetInt(QualityKey, -1);
            if (quality >= 0) SetQuality(quality, false);
        }

        public static void SetVolume(float volume, bool save = true)
        {
            Volume = Mathf.Clamp01(Mathf.Round(volume * 10f) / 10f);
            ApplyListener();
            if (save) Save(VolumeKey, Volume);
        }

        /// <summary>Liga ou desliga o silêncio do menu (<see cref="MenuSilence"/>).</summary>
        public static void SetMenuSilence(bool silent)
        {
            if (MenuSilence == silent && AudioListener.volume == (silent ? 0f : Volume)) return;
            MenuSilence = silent;
            ApplyListener();
        }

        static void ApplyListener() => AudioListener.volume = MenuSilence ? 0f : Volume;

        public static void SetMouseScale(float scale, bool save = true)
        {
            MouseScale = Mathf.Clamp(Mathf.Round(scale * 4f) / 4f, 0.25f, 3f);
            if (save) Save(MouseKey, MouseScale);
        }

        public static void SetQuality(int level, bool save = true)
        {
            int count = QualitySettings.names.Length;
            if (count == 0) return;
            level = Mathf.Clamp(level, 0, count - 1);
            QualitySettings.SetQualityLevel(level, true);
            if (save)
            {
                PlayerPrefs.SetInt(QualityKey, level);
                PlayerPrefs.Save();
            }
        }

        public static string QualityName
        {
            get
            {
                string[] names = QualitySettings.names;
                int level = Quality;
                return level >= 0 && level < names.Length ? names[level] : "?";
            }
        }

        static void Save(string key, float value)
        {
            PlayerPrefs.SetFloat(key, value);
            PlayerPrefs.Save();
        }
    }
}
