using System;
using System.IO;
using BackRooms.Entities;
using UnityEditor;
using UnityEngine;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// Grava em arquivos WAV os sons da entidade, que no jogo são sintetizados na hora. Serve
    /// para ouvir e ajustar cada um sem precisar jogar até ela aparecer.
    /// Menu BackRooms > Exportar sons da entidade (salva em Logs/Sons, fora do Git).
    /// </summary>
    public static class EntitySounds
    {
        [MenuItem("BackRooms/Exportar sons da entidade")]
        public static void ExportFromMenu()
        {
            EditorUtility.RevealInFinder(Export(DefaultFolder()));
        }

        /// <summary>Uso em linha de comando. Aceita -soundOut &lt;pasta&gt;.</summary>
        public static void BatchExport()
        {
            try
            {
                string folder = DefaultFolder();
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                    if (args[i] == "-soundOut") folder = args[i + 1];

                Export(folder);
                Level0AutoUpdate.OpenGameScene();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        static string DefaultFolder()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "Sons"));
        }

        static string Export(string folder)
        {
            Directory.CreateDirectory(folder);

            Save(folder, "entidade_rugido_de_ataque", EntityAudio.CreateRoar());
            Save(folder, "entidade_guincho_de_perseguicao", EntityAudio.CreateScreech());
            Save(folder, "entidade_passo", EntityAudio.CreateStep());
            Save(folder, "entidade_ronco", EntityAudio.CreateDrone());

            Debug.Log("[BackRooms] Sons da entidade salvos em " + folder);
            return folder;
        }

        // WAV mono de 16 bits.
        static void Save(string folder, string name, AudioClip clip)
        {
            var samples = new float[clip.samples * clip.channels];
            clip.GetData(samples, 0);

            using (var stream = new FileStream(Path.Combine(folder, name + ".wav"), FileMode.Create))
            using (var writer = new BinaryWriter(stream))
            {
                int bytes = samples.Length * 2;

                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + bytes);
                writer.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)clip.channels);
                writer.Write(clip.frequency);
                writer.Write(clip.frequency * clip.channels * 2);
                writer.Write((short)(clip.channels * 2));
                writer.Write((short)16);
                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(bytes);

                foreach (float sample in samples)
                    writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(sample, -1f, 1f) * 32767f));
            }

            UnityEngine.Object.DestroyImmediate(clip);
        }
    }
}
