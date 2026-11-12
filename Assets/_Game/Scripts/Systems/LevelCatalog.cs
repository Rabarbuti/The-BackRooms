using System;
using UnityEngine;

namespace BackRooms
{
    /// <summary>Um nível jogável: o que o menu mostra e a cena que ele carrega.</summary>
    public sealed class LevelInfo
    {
        public readonly int Index;
        public readonly string Name;
        public readonly string Subtitle;
        /// <summary>Nome da cena (precisa estar na lista de cenas da build).</summary>
        public readonly string Scene;

        public LevelInfo(int index, string name, string subtitle, string scene)
        {
            Index = index;
            Name = name;
            Subtitle = subtitle;
            Scene = scene;
        }
    }

    /// <summary>
    /// Os níveis do jogo, em ordem. A ordem é a de progressão: zerar um libera o seguinte.
    /// Nível novo entra aqui e na lista de cenas da build (depois do Level 0).
    /// </summary>
    public static class LevelCatalog
    {
        public static readonly LevelInfo[] Levels =
        {
            new LevelInfo(0, "Level 0", "Os corredores amarelos", "Level0_Test"),
            new LevelInfo(1, "Level 1", "O Mostruário (teste)", "Level1_Test"),
            new LevelInfo(2, "Level 2", "As Piscinas (teste)", "Level2_Test")
        };

        public static int Count => Levels.Length;

        public static bool IsValid(int index) => index >= 0 && index < Levels.Length;

        public static LevelInfo Get(int index) => IsValid(index) ? Levels[index] : null;

        /// <summary>Índice do nível cuja cena tem esse nome, ou -1.</summary>
        public static int IndexOfScene(string sceneName)
        {
            for (int i = 0; i < Levels.Length; i++)
                if (Levels[i].Scene == sceneName) return i;
            return -1;
        }
    }

    /// <summary>
    /// Progresso de quem joga nesta máquina: quantos níveis seguidos já zerou, guardado nas
    /// preferências do Unity. Ficam liberados os zerados e o próximo. No modo desenvolvedor
    /// (editor, build de desenvolvimento ou -brDev na linha de comando) tudo fica liberado.
    /// </summary>
    public static class LevelProgress
    {
        public const string ClearedKey = "BackRooms.NiveisZerados";

        /// <summary>Disparado com o índice do nível quando o jogador local o zera.</summary>
        public static event Action<int> LevelCleared;

        /// <summary>Força o modo desenvolvedor ligado ou desligado (testes); null = automático.</summary>
        public static bool? DevModeOverride;

        static bool? _devArg;

        public static bool DevMode
        {
            get
            {
                if (DevModeOverride.HasValue) return DevModeOverride.Value;
                if (Application.isEditor || Debug.isDebugBuild) return true;
                if (!_devArg.HasValue) _devArg = Array.IndexOf(Environment.GetCommandLineArgs(), "-brDev") >= 0;
                return _devArg.Value;
            }
        }

        /// <summary>Quantos níveis, a partir do Level 0, já foram zerados.</summary>
        public static int ClearedCount => Mathf.Clamp(PlayerPrefs.GetInt(ClearedKey, 0), 0, LevelCatalog.Count);

        public static bool IsCleared(int index) => index >= 0 && index < ClearedCount;

        public static bool IsUnlocked(int index) => LevelCatalog.IsValid(index) && (DevMode || index <= ClearedCount);

        /// <summary>Marca o nível como zerado (e, com isso, libera o seguinte).</summary>
        public static void MarkCleared(int index)
        {
            if (!LevelCatalog.IsValid(index)) return;

            if (index + 1 > ClearedCount)
            {
                PlayerPrefs.SetInt(ClearedKey, index + 1);
                PlayerPrefs.Save();
            }

            Debug.Log($"[BackRooms] {LevelCatalog.Get(index).Name} zerado. Níveis zerados: {ClearedCount}.");
            LevelCleared?.Invoke(index);
        }
    }
}
