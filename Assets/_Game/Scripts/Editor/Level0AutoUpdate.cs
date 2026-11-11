using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// Vigia do editor para dois descuidos que fazem o jogo "não aparecer":
    ///
    /// 1. Conteúdo velho. A cena do Level 0, as texturas e os materiais são gerados pelo
    ///    Level0Setup. Quando o gerador muda, o que está salvo fica para trás e o jogo roda
    ///    código novo sobre conteúdo antigo. Compara a versão gravada na cena com
    ///    <see cref="Level0Setup.ContentVersion"/> e oferece recriar tudo.
    ///
    /// 2. Cena errada aberta. Se o editor abre numa cena nova e vazia (acontece depois de
    ///    rodar ferramentas por linha de comando), apertar Play não mostra nada. Abre a cena
    ///    do jogo sozinho e, se alguém der Play numa cena sem nome, troca para a do jogo.
    /// </summary>
    [InitializeOnLoad]
    public static class Level0AutoUpdate
    {
        public const string ScenePath = "Assets/_Game/Scenes/Level0_Test.unity";
        const string AskedKey = "BackRooms.Level0AutoUpdate.Asked";

        static Level0AutoUpdate()
        {
            if (Application.isBatchMode) return;

            EditorApplication.delayCall += OnEditorReady;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        /// <summary>
        /// Verdadeiro se a cena salva foi gerada por uma versão anterior do Level0Setup.
        /// Lê o arquivo da cena direto, então funciona com qualquer cena aberta.
        /// </summary>
        public static bool IsStale(out int savedVersion)
        {
            savedVersion = 0;
            if (!File.Exists(ScenePath)) return false;

            Match match = Regex.Match(File.ReadAllText(ScenePath), @"contentVersion:\s*(-?\d+)");
            if (match.Success) int.TryParse(match.Groups[1].Value, out savedVersion);

            return savedVersion != Level0Setup.ContentVersion;
        }

        /// <summary>
        /// Abre a cena do jogo, se existir. As ferramentas de linha de comando chamam isto
        /// antes de fechar, para o editor lembrar dela na próxima abertura.
        /// </summary>
        public static bool OpenGameScene()
        {
            if (!File.Exists(ScenePath)) return false;
            if (SceneManager.GetActiveScene().path == ScenePath) return true;

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            return true;
        }

        // Cena que nunca foi salva: é a "Untitled" que o editor cria quando não tem o que reabrir.
        static bool ActiveSceneIsUntitled()
        {
            return string.IsNullOrEmpty(SceneManager.GetActiveScene().path);
        }

        static void OnEditorReady()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            // Editor aberto numa cena vazia e intocada: vai direto para a cena do jogo.
            if (ActiveSceneIsUntitled() && !SceneManager.GetActiveScene().isDirty && OpenGameScene())
                Debug.Log("[BackRooms] O editor estava numa cena vazia; abri " + ScenePath + ".");

            OfferRebuild();
        }

        static void OfferRebuild()
        {
            if (SessionState.GetBool(AskedKey, false)) return;
            if (!IsStale(out int saved)) return;

            SessionState.SetBool(AskedKey, true);

            bool rebuild = EditorUtility.DisplayDialog(
                "The BackRooms — Level 0 desatualizado",
                $"O gerador do Level 0 mudou (versão {Level0Setup.ContentVersion}) e a cena salva é da versão {saved}.\n\n" +
                "Enquanto isso o jogo roda com texturas e materiais antigos — a sala inicial, por exemplo, aparece suja.\n\n" +
                "Recriar agora a cena, as texturas e os materiais? Leva alguns segundos.",
                "Recriar agora", "Depois");

            if (!rebuild) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Level0Setup.BuildScene(regenerateAssets: true);
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingEditMode) return;

            // Play numa cena sem nome não mostra o jogo. Cancela, abre a cena certa e dá Play de novo.
            if (ActiveSceneIsUntitled() && File.Exists(ScenePath))
            {
                EditorApplication.isPlaying = false;
                EditorApplication.delayCall += SwitchToGameSceneAndPlay;
                return;
            }

            if (IsStale(out int saved))
                Debug.LogWarning($"[BackRooms] A cena do Level 0 é da versão {saved} do gerador e a atual é {Level0Setup.ContentVersion}. " +
                                 "Use BackRooms > Montar Level 0 > Recriar tudo para atualizar texturas, materiais e a cena.");
        }

        static void SwitchToGameSceneAndPlay()
        {
            if (EditorApplication.isPlaying) return;

            // Se a cena sem nome tiver alterações, o Unity pergunta se quer salvar antes de trocar.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!OpenGameScene()) return;

            Debug.Log("[BackRooms] Play pedido numa cena vazia; abri " + ScenePath + " e iniciei o jogo nela.");
            EditorApplication.EnterPlaymode();
        }

        /// <summary>Uso em linha de comando: registra no log se a cena está desatualizada.</summary>
        public static void BatchReport()
        {
            bool stale = IsStale(out int saved);
            Debug.Log($"[BackRooms] Verificação de versão: cena={saved} gerador={Level0Setup.ContentVersion} desatualizada={stale}");
            OpenGameScene();
            EditorApplication.Exit(0);
        }
    }
}
