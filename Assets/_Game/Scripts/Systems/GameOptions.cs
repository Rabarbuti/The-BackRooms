using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Rendering;

namespace BackRooms
{
    /// <summary>
    /// As opções desta máquina, guardadas em <c>PlayerPrefs</c> e aplicadas ao abrir o jogo: volume
    /// geral, sensibilidade do mouse (multiplica a do <c>PlayerController</c>) e qualidade (os três
    /// perfis do HDRP em ProjectSettings/QualitySettings: High Fidelity, Balanced, Performant).
    /// A tela fica no menu (<c>ConnectionMenu</c>, botão Opções). Na primeira execução (sem qualidade
    /// salva) com placa de vídeo integrada, começa no Performant (<see cref="FirstRunQuality"/>).
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

        /// <summary>Nome do perfil escolhido sozinho para placa integrada na primeira execução.</summary>
        public const string IntegratedProfile = "Performant";

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
            else FirstRunQuality();
        }

        /// <summary>
        /// Sem qualidade salva (primeira execução, ou o jogador nunca mexeu em Options): com placa de
        /// vídeo integrada, aplica o Performant, como a tela de Options faria, mas sem gravar; assim a
        /// conta se refaz a cada abertura até o jogador escolher um perfil, e a escolha dele (gravada
        /// por <see cref="SetQuality"/>) nunca é sobrescrita. Placa dedicada ou na dúvida: nada muda
        /// (abre no perfil padrão do projeto). No editor e sem placa (-batchmode, -nographics) não
        /// faz nada: no editor o perfil ativo é o do projeto.
        /// </summary>
        static void FirstRunQuality()
        {
            if (Application.isEditor || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            string name = SystemInfo.graphicsDeviceName, vendor = SystemInfo.graphicsDeviceVendor;
            int memory = SystemInfo.graphicsMemorySize;
            bool integrated = IsIntegratedGpu(name, vendor, memory);
            int target = integrated ? System.Array.IndexOf(QualitySettings.names, IntegratedProfile) : -1;
            if (target >= 0) SetQuality(target, false);
            Debug.Log($"[BackRooms] Placa de vídeo: {name} ({vendor}, {memory} MB), {(integrated ? "integrada" : "dedicada ou desconhecida")}; sem qualidade salva, perfil {QualityName}{(target >= 0 ? " (escolhido pela placa)" : " (o padrão)")}.");
        }

        static readonly Regex Discrete = new Regex(@"nvidia|geforce|quadro|tesla|\brtx\b|\bgtx\b|\bt\d{3,4}\b|\barc\b|\brx\b|radeon pro|firepro|instinct", RegexOptions.CultureInvariant);
        static readonly Regex IntelIntegrated = new Regex(@"\b(uhd|hd|iris)\b.*graphics|\biris\b", RegexOptions.CultureInvariant);
        static readonly Regex AmdIntegrated = new Regex(@"^(amd )?radeon (graphics|vega \d+ graphics|\d{3}m graphics|\d{3}m)$", RegexOptions.CultureInvariant);

        /// <summary>
        /// A placa de vídeo é integrada (Intel UHD/HD/Iris, AMD "Radeon Graphics" sem modelo, Vega ou
        /// 680M/780M de APU)? Conservadora: na dúvida, não. NVIDIA, AMD RX/Pro e Intel Arc nunca são.
        /// A memória só reforça um caso antigo (Intel ou AMD "... Graphics" com até 512 MB); memória
        /// grande não diz nada, porque a integrada informa a memória compartilhada.
        /// </summary>
        public static bool IsIntegratedGpu(string deviceName, string vendor, int memoryMB)
        {
            string name = Normalize(deviceName), maker = Normalize(vendor);
            if (name.Length == 0 || name.Contains("null")) return false;
            if (Discrete.IsMatch(name) || maker.Contains("nvidia")) return false;
            bool intel = name.Contains("intel") || maker.Contains("intel");
            bool amd = name.Contains("amd") || name.Contains("radeon") || maker.Contains("amd") || maker.Contains("advanced micro") || maker == "ati" || maker.StartsWith("ati ");
            if (intel && IntelIntegrated.IsMatch(name)) return true;
            if (amd && AmdIntegrated.IsMatch(name)) return true;
            return (intel || amd) && name.EndsWith(" graphics") && memoryMB > 0 && memoryMB <= 512;
        }

        static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.ToLowerInvariant().Replace("(tm)", " ").Replace("(r)", " ").Replace("™", " ").Replace("®", " ");
            return Regex.Replace(text, @"\s+", " ").Trim();
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
