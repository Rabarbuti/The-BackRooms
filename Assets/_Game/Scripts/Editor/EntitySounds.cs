using System;
using System.IO;
using BackRooms.Entities;
using BackRooms.Player;
using UnityEditor;
using UnityEngine;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// Grava em arquivos WAV os sons da entidade e os passos do jogador por superfície (carpete,
    /// azulejo, água rasa, nado, debaixo d'água e o espirro de entrada), que no jogo são
    /// sintetizados na hora. Serve para ouvir e ajustar cada um sem precisar jogar.
    /// Menu BackRooms > Exportar sons (entidade e passos), salva em Logs/Sons, fora do Git.
    /// Linha de comando: EntitySounds.BatchExport.
    /// </summary>
    public static class EntitySounds
    {
        [MenuItem("BackRooms/Exportar sons (entidade e passos)")]
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
            Save(folder, "capitao_toc", CaptainAudio.CreateKnock());
            Save(folder, "capitao_arrasta", CaptainAudio.CreateDrag());
            Save(folder, "capitao_jingle", BackRooms.Audio.ShopMusic.CreateJingle());
            // O Undertow (Level 2), refeito em 21/11/2026: os dois rugidos (ao ouvir, no bote e quando
            // a maré sobe), o rosnado de aproximação (em laço), o gorgolejo de quem surge de um ralo e
            // o puxão ao pegar.
            Save(folder, "undertow_rugido_1", UndertowAudio.CreateRoar(5101));
            Save(folder, "undertow_rugido_2", UndertowAudio.CreateRoar(5102));
            Save(folder, "undertow_rosnado", UndertowAudio.CreateGrowl());
            Save(folder, "undertow_gorgolejo", UndertowAudio.CreateGurgle());
            Save(folder, "undertow_puxao", UndertowAudio.CreateGrab());
            // O Zelador (Level 3): desde 23/11/2026 a vassoura seca, uma passada a cada dois passos, e a
            // voz de caça (rosnado rouco, enquanto corre atrás de alguém).
            for (int i = 0; i < 4; i++) Save(folder, "zelador_vassoura_" + i, JanitorAudio.CreateSweep(i));
            for (int i = 0; i < 4; i++) Save(folder, "zelador_voz_" + i, JanitorAudio.CreateGrowl(i));
            Save(folder, "interruptor_estalo", LightSwitches.CreateClack());
            Save(folder, "reator_falhando", BackRooms.World.OfficeSounds.Sputter);
            // O Ascensorista (Level 3): bengala, arrasto, estalos da boca, respiração; e o plim e o ronco do elevador.
            Save(folder, "ascensorista_bengala", AttendantAudio.Tap);
            Save(folder, "ascensorista_arrasto", AttendantAudio.Scrape);
            Save(folder, "ascensorista_estalos", AttendantAudio.Clicks);
            Save(folder, "ascensorista_respiracao", AttendantAudio.Breath);
            Save(folder, "elevador_plim", BackRooms.World.OfficeSounds.Chime);
            Save(folder, "elevador_ronco", BackRooms.World.OfficeSounds.Rumble);
            Save(folder, "loja_musica", BackRooms.Audio.ShopMusic.CreateMuzak());
            for (int i = 0; i < 3; i++)
            {
                Save(folder, "plastico_bolha_" + i, BackRooms.World.BubbleWrap.CreatePops(i));
                Save(folder, "passo_laminado_" + i, Footsteps.CreateClip(StepSurface.Laminate, i));
                Save(folder, "passo_vinilico_" + i, Footsteps.CreateClip(StepSurface.Vinyl, i));
            }

            // Passos do jogador por superfície (Footsteps), três variações de cada, e o espirro
            // de entrar ou sair da água. O volume relativo já está no clipe (pico de cada um).
            for (int i = 0; i < 3; i++)
            {
                Save(folder, "passo_carpete_" + i, Footsteps.CreateClip(StepSurface.Carpet, i));
                Save(folder, "passo_azulejo_" + i, Footsteps.CreateClip(StepSurface.Tile, i));
                Save(folder, "passo_agua_rasa_" + i, Footsteps.CreateClip(StepSurface.Wade, i));
                Save(folder, "nado_bracada_" + i, Footsteps.CreateClip(StepSurface.Swim, i));
                Save(folder, "debaixo_dagua_" + i, Footsteps.CreateClip(StepSurface.Underwater, i));
                // O piso duro do Escritório (superfície Tile, som próprio).
                Save(folder, "passo_escritorio_" + i, Footsteps.CreateOfficeClip(i));
            }
            for (int i = 0; i < 2; i++) Save(folder, "espirro_entrada_" + i, Footsteps.CreateSplash(i));

            // Os pisos secos também têm um conjunto agachado (força 0,25) e um correndo (1); os
            // sem sufixo acima são andando (0,55).
            for (int i = 0; i < 2; i++)
            {
                foreach (var (surface, name) in new[] { (StepSurface.Carpet, "carpete"), (StepSurface.Laminate, "laminado"),
                                                        (StepSurface.Vinyl, "vinilico"), (StepSurface.Tile, "azulejo") })
                {
                    Save(folder, $"passo_{name}_agachado_{i}", Footsteps.CreateClip(surface, i, 0.25f));
                    Save(folder, $"passo_{name}_corrida_{i}", Footsteps.CreateClip(surface, i, 1f));
                }
                Save(folder, $"passo_escritorio_agachado_{i}", Footsteps.CreateOfficeClip(i, 0.25f));
                Save(folder, $"passo_escritorio_corrida_{i}", Footsteps.CreateOfficeClip(i, 1f));
            }

            // A maré das Piscinas (PoolTide): sirene e buzina do alarme, água correndo, glub e ralo.
            Save(folder, "mare_sirene", BackRooms.World.PoolTide.CreateSiren());
            Save(folder, "mare_buzina", BackRooms.World.PoolTide.CreateHorn());
            Save(folder, "mare_correnteza", BackRooms.World.PoolTide.CreateRush());
            Save(folder, "mare_glub", BackRooms.World.PoolTide.CreateGlub());
            Save(folder, "ralo_gorgolejo", BackRooms.World.PoolTide.CreateGurgle());

            // Escritório (Level 3): o ambiente em laço (OfficeAmbience), as portas de armário batendo
            // (LockerBangs) e o clique dos botões do elevador (ElevatorControl).
            Save(folder, "escritorio_ar", BackRooms.Audio.OfficeAmbience.CreateAir());
            Save(folder, "escritorio_reator", BackRooms.Audio.OfficeAmbience.CreateBallast());
            Save(folder, "escritorio_difusor", BackRooms.Audio.OfficeAmbience.CreateVent());
            for (int i = 0; i < 3; i++) Save(folder, "armario_bate_" + i, BackRooms.World.LockerBangs.CreateBang(i));
            Save(folder, "elevador_botao", BackRooms.World.ElevatorControl.CreateClick());

            Debug.Log("[BackRooms] Sons da entidade e dos passos salvos em " + folder);
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
