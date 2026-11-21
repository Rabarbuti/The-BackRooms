using System.Collections.Generic;
using BackRooms.Net;
using UnityEngine;

namespace BackRooms.World
{
    /// <summary>Uma linha da lista de tarefas do menu de pausa.</summary>
    public struct LevelTask
    {
        /// <summary>O que fazer, em linguagem de jogador (com a contagem, se houver).</summary>
        public string Text;
        /// <summary>Uma linha menor embaixo: como, onde, o prazo. Pode ser nula.</summary>
        public string Detail;
        public bool Done;

        public LevelTask(string text, bool done, string detail = null)
        {
            Text = text;
            Done = done;
            Detail = detail;
        }
    }

    /// <summary>
    /// Quem tem tarefas para o jogador (o objetivo de cada fase: <see cref="SwitchObjective"/>,
    /// <see cref="ExitLock"/>). Registra-se em <see cref="LevelTasks"/> e o menu de pausa só desenha
    /// o que vier: fase nova ganha tarefas sem mexer no menu.
    /// </summary>
    public interface ITaskSource
    {
        /// <summary>Acrescenta as tarefas desta fase, na ordem, com o estado de agora.</summary>
        void AddTasks(List<LevelTask> tasks);

        /// <summary>Acrescenta as teclas próprias desta fase (tecla, o que faz).</summary>
        void AddKeys(List<(string key, string action)> keys);
    }

    /// <summary>
    /// A fonte única das tarefas da fase aberta: junta o que cada <see cref="ITaskSource"/> registrado
    /// publica. Também monta a tarefa comum da porta de saída (<see cref="ExitTask"/>).
    /// </summary>
    public static class LevelTasks
    {
        // Textos que o jogador lê (a língua do jogo é o inglês).
        const string WaitingText = "Waiting at the door ({0}/{1}): everyone has to be there";
        const string FindExitText = "Find the way out";

        static readonly List<ITaskSource> Sources = new List<ITaskSource>();

        public static void Register(ITaskSource source)
        {
            if (source != null && !Sources.Contains(source)) Sources.Add(source);
        }

        public static void Unregister(ITaskSource source) => Sources.Remove(source);

        /// <summary>As tarefas de agora. Sem nenhum objetivo na fase, só "achar a saída".</summary>
        public static void Collect(List<LevelTask> tasks)
        {
            tasks.Clear();
            Sources.RemoveAll(s => s == null || s is Object o && o == null);
            foreach (ITaskSource source in Sources) source.AddTasks(tasks);
            if (tasks.Count == 0) tasks.Add(ExitTask(FindExitText, false));
        }

        /// <summary>As teclas próprias das fontes registradas.</summary>
        public static void CollectKeys(List<(string key, string action)> keys)
        {
            Sources.RemoveAll(s => s == null || s is Object o && o == null);
            foreach (ITaskSource source in Sources) source.AddKeys(keys);
        }

        /// <summary>
        /// A tarefa de sair: <paramref name="text"/> e, se o jogador desta máquina já espera na porta,
        /// quantos faltam. <paramref name="locked"/> só muda o detalhe (a porta ainda trancada).
        /// </summary>
        public static LevelTask ExitTask(string text, bool locked, string lockedDetail = null)
        {
            LevelExit exit = LevelExit.Instance;
            if (exit != null && exit.LocalWaiting)
            {
                PlayerNetwork.CountAtExit(out int waiting, out int total);
                return new LevelTask(text, false, string.Format(WaitingText, waiting, total));
            }
            return new LevelTask(text, false, locked ? lockedDetail : null);
        }

        /// <summary>Quantos jogadores há na sessão, visto desta máquina (pelo menos 1).</summary>
        public static int PlayerCount
        {
            get
            {
                GameSession session = GameSession.Instance;
                if (session != null && session.IsHosting) return Mathf.Max(1, session.PlayerCount);
                return Mathf.Max(1, PlayerNetwork.All.Count);
            }
        }
    }
}
