using System.IO;
using BackRooms.World;
using UnityEditor;
using UnityEngine;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// Fotos de conferência dos textos pintados no mundo (a língua do jogo é o inglês), fora do
    /// Play: as etiquetas do Level 1 (<see cref="ExitLock"/>), as placas de papelão
    /// (<see cref="PropLayer.SignTexture"/>) e os cartazes de dicas das outras fases
    /// (<see cref="SwitchObjective.NoteTexture"/>). Cada desenho sai ampliado (pontos nítidos) em
    /// <c>Logs/Previews/ingles/textura_*.png</c>.
    ///
    /// Linha de comando: -executeMethod BackRooms.EditorTools.EnglishTextPreview.BatchCapture.
    /// Linha "[BackRooms] Fotos dos textos".
    /// </summary>
    public static class EnglishTextPreview
    {
        const string Folder = "Logs/Previews/ingles";
        const int Zoom = 8;

        [MenuItem("BackRooms/Fotografar textos do mundo (inglês)")]
        public static void CaptureFromMenu()
        {
            int count = Capture();
            EditorUtility.RevealInFinder(Folder);
            Debug.Log($"[BackRooms] Fotos dos textos: {count} em {Folder}.");
        }

        public static void BatchCapture()
        {
            int count = 0;
            int code = 0;
            try { count = Capture(); }
            catch (System.Exception e) { Debug.LogException(e); code = 1; }
            Debug.Log($"[BackRooms] Fotos dos textos " + (code == 0 ? "PRONTAS" : "FALHARAM") + $": {count} em {Folder}.");
            EditorApplication.Exit(code);
        }

        static int Capture()
        {
            Directory.CreateDirectory(Folder);
            int count = 0;
            int[] digits = { 9, 4, 3 };
            for (int i = 0; i < 3; i++) count += Save(ExitLock.TagTexture(digits[i], i + 1), "textura_etiqueta_" + (i + 1));
            foreach (var sign in PropLayer.SignTexts)
                count += Save(PropLayer.SignTexture(sign.text, sign.ink, sign.paper), "textura_placa_" + sign.text.Replace(' ', '_').ToLowerInvariant());
            string[] levels = { "level0", "level2", "level3" };
            string[][] tips = SwitchObjective.AllTips;
            for (int i = 0; i < tips.Length; i++) count += Save(SwitchObjective.NoteTexture(tips[i]), "textura_cartaz_" + levels[i]);
            return count;
        }

        // Amplia sem suavizar (cada ponto vira um quadrado) e grava em PNG.
        static int Save(Texture2D source, string name)
        {
            int w = source.width * Zoom, h = source.height * Zoom;
            Color32[] from = source.GetPixels32();
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    pixels[y * w + x] = from[(y / Zoom) * source.width + x / Zoom];
            var big = new Texture2D(w, h, TextureFormat.RGBA32, false);
            big.SetPixels32(pixels);
            big.Apply(false);
            File.WriteAllBytes(Path.Combine(Folder, name + ".png"), big.EncodeToPNG());
            Object.DestroyImmediate(big);
            Object.DestroyImmediate(source);
            return 1;
        }
    }
}
