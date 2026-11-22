using System.Collections.Generic;
using BackRooms.Net;
using UnityEngine;

namespace BackRooms.World
{
    /// <summary>
    /// Um sistema de interagíveis de mira + E (interruptores, botões, pontos do objetivo, pilhas,
    /// teclado, cadeiras, esconderijos). Diz qual dos seus alvos o jogador mira melhor agora.
    /// </summary>
    public interface IAimSource
    {
        /// <summary>
        /// O melhor alvo deste sistema para a mira <paramref name="view"/>: índice (do próprio sistema)
        /// e nota (graus do centro da tela; menor ganha). Falso se nenhum vale. Sem efeito colateral:
        /// a <see cref="InteractAim"/> chama uma vez por quadro.
        /// </summary>
        bool AimCandidate(in InteractAim.View view, out int index, out float score);
    }

    /// <summary>
    /// A mira comum dos interagíveis (pedido de 21/11/2026: o interruptor exigia mirar certinho).
    ///
    /// - <b>Tolerante:</b> vale o raio da câmera bater no alvo (como antes) <b>ou</b> o alvo estar a até
    ///   <see cref="ConeDegrees"/> do centro da tela (de perto, a até <see cref="NearRadius"/> da linha
    ///   da mira), dentro do alcance e com visada livre: o raio do olho até o alvo não pode bater em
    ///   nada antes (parede no meio não vale). Sem colisor novo: nada muda para a física nem para o
    ///   <c>Level0ColliderCheck</c>.
    /// - <b>Um só vencedor:</b> todos os sistemas registrados disputam e só o alvo mais perto do centro
    ///   da mira ganha o quadro (<see cref="Winner"/>); os outros não mostram aviso nem agem no E.
    /// - Os raios ignoram o próprio jogador (o padrão de <c>PenTool.TryAim</c>: o colisor do corpo
    ///   anda um passo atrasado) e os gatilhos.
    /// </summary>
    public static class InteractAim
    {
        /// <summary>Meia abertura do cone da mira, em graus.</summary>
        public const float ConeDegrees = 9f;
        /// <summary>De perto o cone fica estreito: vale também o alvo a até isto (m) da linha da mira.</summary>
        public const float NearRadius = 0.2f;
        /// <summary>Alcance do raio direto (o maior dos alcances dos sistemas, com folga).</summary>
        public const float MaxReach = 3f;
        /// <summary>Nota de quem o raio acerta em cheio, num alvo grande (o centro dele pode estar longe da mira).</summary>
        public const float DirectScore = ConeDegrees * 0.5f;

        /// <summary>O olhar do jogador desta máquina neste quadro.</summary>
        public struct View
        {
            public Vector3 Origin;
            public Vector3 Forward;
            /// <summary>A raiz do próprio jogador (os raios a atravessam).</summary>
            public Transform Self;
            /// <summary>O primeiro colisor que o raio da câmera acerta (nulo se nenhum até <see cref="MaxReach"/>).</summary>
            public Collider Direct;
            public float DirectDistance;
            public PlayerNetwork Local;
        }

        static readonly List<IAimSource> Sources = new List<IAimSource>();
        static readonly RaycastHit[] Hits = new RaycastHit[24];

        static int _frame = -1;
        static IAimSource _winner;
        static int _index = -1;

        public static void Register(IAimSource source)
        {
            if (source != null && !Sources.Contains(source)) Sources.Add(source);
            _frame = -1;
        }

        public static void Unregister(IAimSource source)
        {
            Sources.Remove(source);
            if (_winner == source) { _winner = null; _index = -1; }
            _frame = -1;
        }

        /// <summary>O índice do alvo de <paramref name="source"/> se ele ganhou a mira neste quadro; senão -1.</summary>
        public static int Winner(IAimSource source)
        {
            Resolve();
            return _winner != null && _winner == source ? _index : -1;
        }

        /// <summary>Algum interagível ganhou a mira neste quadro (a porta de saída cede a vez).</summary>
        public static bool Any
        {
            get
            {
                Resolve();
                return _winner != null;
            }
        }

        static void Resolve()
        {
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;
            _winner = null;
            _index = -1;
            PlayerNetwork local = PlayerNetwork.Local;
            // Sentado, escondido ou voltando ao início, o E é de levantar ou sair (sem mira): nada ganha.
            BackRooms.Player.PlayerController body = local != null ? local.controller : null;
            if (body == null || body.IsSeated || body.InFurniture || body.IsReturning) return;
            if (!Look(local, out View view)) return;

            float best = float.PositiveInfinity;
            for (int i = Sources.Count - 1; i >= 0; i--)
            {
                IAimSource source = Sources[i];
                if (source == null || (source is Object o && o == null))
                {
                    Sources.RemoveAt(i);
                    continue;
                }
                if (source is Behaviour b && !b.isActiveAndEnabled) continue;
                if (!source.AimCandidate(view, out int index, out float score) || index < 0 || score >= best) continue;
                best = score;
                _winner = source;
                _index = index;
            }
        }

        /// <summary>O olhar do jogador: olho, direção e o primeiro colisor na mira (sem ele mesmo).</summary>
        public static bool Look(PlayerNetwork local, out View view)
        {
            view = default;
            if (local == null || !local.InGame || local.playerCamera == null) return false;
            Transform eye = local.playerCamera.transform;
            view.Origin = eye.position;
            view.Forward = eye.forward;
            view.Self = local.transform.root;
            view.Local = local;

            int count = Physics.RaycastNonAlloc(view.Origin, view.Forward, Hits, MaxReach, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (Hits[i].collider.transform.root == view.Self || Hits[i].distance >= best) continue;
                best = Hits[i].distance;
                view.Direct = Hits[i].collider;
                view.DirectDistance = Hits[i].distance;
            }
            return true;
        }

        /// <summary>
        /// A nota de um alvo (graus do centro da mira; menor é melhor), ou infinito se não vale.
        /// <paramref name="owner"/> é a peça do alvo: colisores debaixo dela contam como o alvo (o raio
        /// direto que bate nela vale; a visada não para nela). <paramref name="point"/> é o meio da peça.
        /// </summary>
        public static float Score(in View view, Transform owner, Vector3 point, float reach)
        {
            if (owner == null) return float.PositiveInfinity;
            Vector3 to = point - view.Origin;
            float distance = to.magnitude;
            float angle = distance > 1e-4f ? Vector3.Angle(view.Forward, to) : 0f;

            // O raio acerta a peça em cheio: vale como sempre valeu.
            if (view.Direct != null && view.DirectDistance <= reach && view.Direct.transform.IsChildOf(owner))
                return Mathf.Min(angle, DirectScore);

            if (distance > reach + 0.15f || angle >= 60f) return float.PositiveInfinity;
            float miss = distance * Mathf.Sin(angle * Mathf.Deg2Rad);
            if (angle > ConeDegrees && miss > NearRadius) return float.PositiveInfinity;
            return Clear(view, owner, point, distance) ? angle : float.PositiveInfinity;
        }

        /// <summary>A peça é o próprio colisor acertado pelo raio direto (dentro do alcance)?</summary>
        public static bool DirectOn(in View view, Transform owner, float reach) =>
            owner != null && view.Direct != null && view.DirectDistance <= reach && view.Direct.transform.IsChildOf(owner);

        // Visada do olho até o ponto: nada no meio além do próprio jogador e da peça do alvo.
        static bool Clear(in View view, Transform owner, Vector3 point, float distance)
        {
            if (distance < 0.02f) return true;
            Vector3 dir = (point - view.Origin) / distance;
            int count = Physics.RaycastNonAlloc(view.Origin, dir, Hits, distance - 0.01f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Transform t = Hits[i].collider.transform;
                if (t.root == view.Self || t.IsChildOf(owner)) continue;
                return false;
            }
            return true;
        }
    }
}
