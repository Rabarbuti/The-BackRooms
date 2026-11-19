using UnityEditor;
using UnityEngine;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// Conferência das opções (<see cref="GameOptions"/>), fora do Play: cada valor é preso aos
    /// limites, aplicado (volume no AudioListener, perfil de qualidade) e guardado em PlayerPrefs; o
    /// que estava salvo antes volta no fim. Linha de comando: -executeMethod
    /// BackRooms.EditorTools.GameOptionsCheck.BatchRun (sai com 0 se passou, 3 se falhou). Linha
    /// "[BackRooms] Conferência das opções".
    /// </summary>
    public static class GameOptionsCheck
    {
        public static void BatchRun()
        {
            string problem = Check();
            Debug.Log("[BackRooms] Conferência das opções " + (problem == null ? "PASSOU: volume, mouse e qualidade presos aos limites, aplicados e guardados" : "FALHOU: " + problem));
            EditorApplication.Exit(problem == null ? 0 : 3);
        }

        [MenuItem("BackRooms/Conferir opções")]
        public static void RunFromMenu()
        {
            string problem = Check();
            EditorUtility.DisplayDialog("Opções", problem ?? "Volume, mouse e qualidade conferidos.", "OK");
        }

        static string Check()
        {
            float volume = PlayerPrefs.GetFloat(GameOptions.VolumeKey, 1f);
            float mouse = PlayerPrefs.GetFloat(GameOptions.MouseKey, 1f);
            int quality = QualitySettings.GetQualityLevel();
            bool hadQuality = PlayerPrefs.HasKey(GameOptions.QualityKey);
            int savedQuality = PlayerPrefs.GetInt(GameOptions.QualityKey, -1);
            try
            {
                GameOptions.SetVolume(0.43f);
                if (!Mathf.Approximately(GameOptions.Volume, 0.4f) || !Mathf.Approximately(AudioListener.volume, 0.4f)) return $"volume 0,43 virou {GameOptions.Volume} (listener {AudioListener.volume})";
                if (!Mathf.Approximately(PlayerPrefs.GetFloat(GameOptions.VolumeKey), 0.4f)) return "o volume não foi guardado";
                GameOptions.SetVolume(7f);
                if (!Mathf.Approximately(GameOptions.Volume, 1f)) return $"volume 7 virou {GameOptions.Volume}";

                GameOptions.SetMouseScale(0.01f);
                if (!Mathf.Approximately(GameOptions.MouseScale, 0.25f)) return $"mouse 0,01 virou {GameOptions.MouseScale}";
                GameOptions.SetMouseScale(1.6f);
                if (!Mathf.Approximately(GameOptions.MouseScale, 1.5f)) return $"mouse 1,6 virou {GameOptions.MouseScale}";

                int last = QualitySettings.names.Length - 1;
                if (last < 2) return $"só {last + 1} perfis de qualidade (eram 3)";
                GameOptions.SetQuality(99);
                if (QualitySettings.GetQualityLevel() != last || PlayerPrefs.GetInt(GameOptions.QualityKey) != last) return "a qualidade não foi presa ao último perfil";

                // Carregar de novo devolve o que foi guardado.
                GameOptions.SetMouseScale(1f, false);
                GameOptions.Load();
                if (!Mathf.Approximately(GameOptions.MouseScale, 1.5f)) return "ao carregar, o mouse não voltou ao guardado";
                return null;
            }
            finally
            {
                GameOptions.SetVolume(volume);
                GameOptions.SetMouseScale(mouse);
                QualitySettings.SetQualityLevel(quality, true);
                if (hadQuality) PlayerPrefs.SetInt(GameOptions.QualityKey, savedQuality);
                else PlayerPrefs.DeleteKey(GameOptions.QualityKey);
                PlayerPrefs.Save();
            }
        }
    }
}
