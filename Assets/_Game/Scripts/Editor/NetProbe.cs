using System;
using System.Collections.Generic;
using BackRooms.Net;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace BackRooms.EditorTools
{
    /// <summary>
    /// Sonda dos testes em dois editores: o host manda, de tempos em tempos, a hora da sessão
    /// (<see cref="GameSession.SessionSeconds"/>) e uns números do estado dele (hash da planta, altura da
    /// água, posição da entidade, pilhas pegas...); o cliente recebe e compara com o que ele mesmo vê
    /// naquele instante. Só existe nos testes: mensagem nomeada própria, registrada no
    /// <see cref="NetworkManager"/> da sessão (nunca no Singleton).
    ///
    /// O relógio: o host manda também o instante, no relógio do Windows (UTC), em que a sessão dele
    /// começou (agora − <see cref="GameSession.SessionSeconds"/>); o cliente calcula o mesmo com o relógio
    /// dele a cada passada do teste (<see cref="Watch"/>) e os dois têm de bater. As duas máquinas
    /// dos testes são a mesma, então o relógio do Windows é o mesmo, e a conta não depende do atraso
    /// da entrega. Medir na chegada da mensagem não serve: depois de um quadro travado do cliente
    /// (gravação, shader compilando) as mensagens acumuladas são lidas todas de uma vez, com o
    /// ServerTime ainda de antes do travamento (visto: −4,4 a +7,2 s, mediana −0,05 s).
    /// </summary>
    public static class NetProbe
    {
        public const string Message = "BackRooms.Teste.Sonda";

        /// <summary>Uma amostra recebida pelo cliente.</summary>
        public struct Sample
        {
            public double HostSeconds;
            public double ClientSeconds;
            public float[] Values;
        }

        public static readonly List<Sample> Received = new List<Sample>();
        public static double MinSkew = double.MaxValue;
        public static double MaxSkew = double.MinValue;
        public static readonly List<double> Skews = new List<double>();
        public static readonly List<double> HostEpochs = new List<double>();
        public static readonly List<double> ClientEpochs = new List<double>();
        public static Action<Sample> OnSample;

        static readonly DateTime Origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        static double Utc => (DateTime.UtcNow - Origin).TotalSeconds;

        static NetworkManager Net(GameSession session) => session != null ? session.GetComponent<NetworkManager>() : null;

        public static void Reset()
        {
            Received.Clear();
            MinSkew = double.MaxValue;
            MaxSkew = double.MinValue;
            Skews.Clear();
            HostEpochs.Clear();
            ClientEpochs.Clear();
            OnSample = null;
        }

        /// <summary>Cliente: passa a ouvir a sonda (chamar logo depois de <c>Join</c>, a cada Join).</summary>
        public static void Listen(GameSession session)
        {
            NetworkManager net = Net(session);
            if (net == null || net.CustomMessagingManager == null) return;
            net.CustomMessagingManager.UnregisterNamedMessageHandler(Message);
            net.CustomMessagingManager.RegisterNamedMessageHandler(Message, (sender, reader) =>
            {
                reader.ReadValueSafe(out double hostSeconds);
                reader.ReadValueSafe(out double hostEpoch);
                if (hostSeconds >= 0.0) HostEpochs.Add(hostEpoch);
                reader.ReadValueSafe(out int count);
                var values = new float[count];
                for (int i = 0; i < count; i++) reader.ReadValueSafe(out values[i]);

                GameSession current = GameSession.Instance;
                double mine = current != null ? current.SessionSeconds : -1.0;
                var sample = new Sample { HostSeconds = hostSeconds, ClientSeconds = mine, Values = values };
                if (hostSeconds >= 0.0 && mine >= 0.0)
                {
                    double skew = mine - hostSeconds;
                    MinSkew = Math.Min(MinSkew, skew);
                    MaxSkew = Math.Max(MaxSkew, skew);
                    Skews.Add(skew);
                }
                Received.Add(sample);
                OnSample?.Invoke(sample);
            });
        }

        /// <summary>Host: manda a hora e <paramref name="values"/> a todos os clientes.</summary>
        public static void Send(GameSession session, params float[] values)
        {
            NetworkManager net = Net(session);
            if (net == null || !net.IsServer || net.CustomMessagingManager == null) return;
            using (var writer = new FastBufferWriter(sizeof(double) * 2 + sizeof(int) + sizeof(float) * values.Length, Allocator.Temp))
            {
                double seconds = session.SessionSeconds;
                writer.WriteValueSafe(seconds);
                writer.WriteValueSafe(Utc - seconds);
                writer.WriteValueSafe(values.Length);
                foreach (float v in values) writer.WriteValueSafe(v);
                foreach (ulong client in net.ConnectedClientsIds)
                    if (client != net.LocalClientId) net.CustomMessagingManager.SendNamedMessage(Message, client, writer);
            }
        }

        /// <summary>Cliente, a cada passada do teste: o instante (UTC) em que a sessão começou, pelo relógio desta máquina.</summary>
        public static void Watch(GameSession session)
        {
            if (session == null || session.IsHosting) return;
            double seconds = session.SessionSeconds;
            if (seconds >= 0.0) ClientEpochs.Add(Utc - seconds);
        }

        static double Median(List<double> list)
        {
            var sorted = new List<double>(list);
            sorted.Sort();
            return sorted[sorted.Count / 2];
        }

        /// <summary>
        /// Os relógios batem? O começo da sessão visto pelo cliente (mediana) a menos de 0,15 s do
        /// visto pelo host, e 90% das passadas do cliente a menos de 0,5 s (um quadro travado conta uma
        /// passada só).
        /// </summary>
        public static bool ClockOk(out string report)
        {
            if (HostEpochs.Count < 5 || ClientEpochs.Count < 10)
            {
                report = $"relogio: amostras de menos (host {HostEpochs.Count}, cliente {ClientEpochs.Count})";
                return false;
            }
            double host = Median(HostEpochs);
            double offset = Median(ClientEpochs) - host;
            int near = 0;
            foreach (double e in ClientEpochs) if (Math.Abs(e - host) < 0.5) near++;
            float share = near / (float)ClientEpochs.Count;
            bool ok = Math.Abs(offset) < 0.15 && share >= 0.9f;
            report = $"relogio: o cliente difere do host em {offset:+0.000;-0.000} s (mediana), {share:P0} de {ClientEpochs.Count} passadas a menos de 0,5 s" +
                     (Skews.Count > 0 ? $"; na chegada das mensagens: mediana {Median(Skews):+0.00;-0.00} s, de {MinSkew:+0.00;-0.00} a {MaxSkew:+0.00;-0.00} s" : "");
            return ok;
        }

        /// <summary>Hash simples (FNV) para comparar listas de números entre as máquinas.</summary>
        public static int Fnv(IEnumerable<int> values)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (int v in values) hash = (hash ^ (uint)v) * 16777619u;
                // Cabe num float sem perder nada: só os 24 bits de baixo.
                return (int)(hash & 0xFFFFFF);
            }
        }

        /// <summary>Hash das posições (em cm) de uma lista de pontos, para comparar entre as máquinas.</summary>
        public static int PointsHash(IEnumerable<Vector3> points)
        {
            var ints = new List<int>();
            foreach (Vector3 p in points)
            {
                ints.Add(Mathf.RoundToInt(p.x * 100f));
                ints.Add(Mathf.RoundToInt(p.y * 100f));
                ints.Add(Mathf.RoundToInt(p.z * 100f));
            }
            return Fnv(ints);
        }

        /// <summary>
        /// Lê "-nome valor" da linha de comando; <paramref name="fallback"/> se não houver. Aceita
        /// também "-nome,valor": a fila chamada de fora do PowerShell (bash, powershell -File) junta o
        /// "-ExtraCopia '-nome','valor'" num argumento só.
        /// </summary>
        public static float Arg(string name, float fallback)
        {
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            var style = System.Globalization.NumberStyles.Float;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == name && i + 1 < args.Length && float.TryParse(args[i + 1], style, culture, out float v)) return v;
                if (args[i].StartsWith(name + ",") && float.TryParse(args[i].Substring(name.Length + 1), style, culture, out v)) return v;
            }
            return fallback;
        }
    }
}
