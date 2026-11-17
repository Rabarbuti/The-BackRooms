using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace BackRooms.World
{
    /// <summary>
    /// Uma luminária de teto: painel emissivo + luzes. Estados próprios: ligada, piscando ou
    /// queimada. Por cima disso vem a fase da energia do andar (<see cref="LightsOut"/>): no
    /// aviso de apagão toda luminária que funciona pisca, e no apagão todas ficam escuras.
    /// </summary>
    public sealed class CeilingLight : MonoBehaviour
    {
        /// <summary>Todas as luminárias do nível construído.</summary>
        public static readonly List<CeilingLight> All = new List<CeilingLight>();

        HDAdditionalLightData[] _lights;
        MeshRenderer _panel;
        Material _onMaterial;
        Material _offMaterial;
        LightState _state;
        PowerPhase _power = PowerPhase.Steady;
        bool _lit;
        float _timer;

        public LightState State => _state;

        /// <summary>Verdadeiro se a luminária está acesa neste instante.</summary>
        public bool IsLit => _lit;

        /// <summary>Verdadeiro se ela acende quando há energia (não está queimada nem é só enfeite).</summary>
        public bool Works => _state == LightState.On || _state == LightState.Flicker;

        /// <summary>De onde sai a luz (dentro da calha, rente ao forro). O Capitão confere se ela o alcança.</summary>
        public Vector3 LightPosition => _lights != null && _lights.Length > 0 && _lights[0] != null ? _lights[0].transform.position : transform.position;

        void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
        }

        void OnDestroy()
        {
            All.Remove(this);
        }

        public void Init(LightState state, HDAdditionalLightData[] lights, MeshRenderer panel, Material onMaterial, Material offMaterial)
        {
            _state = state;
            _lights = lights;
            _panel = panel;
            _onMaterial = onMaterial;
            _offMaterial = offMaterial;

            // Nasce no meio de um aviso ou de um apagão? Já entra na fase certa.
            if (!All.Contains(this)) All.Add(this);
            SetPower(LightsOut.Instance != null ? LightsOut.Instance.Phase : PowerPhase.Steady);
        }

        /// <summary>Fase da energia do andar. Quem chama é o <see cref="LightsOut"/>.</summary>
        public void SetPower(PowerPhase power)
        {
            _power = power;
            _timer = 0f;

            if (power == PowerPhase.Dark || !Works)
            {
                Apply(false);
                return;
            }

            Apply(true);
        }

        void Update()
        {
            // Só pisca quem funciona, e só se for luminária defeituosa ou se o andar estiver avisando.
            if (!Works || _power == PowerPhase.Dark) return;
            if (_power == PowerPhase.Steady && _state != LightState.Flicker) return;

            _timer -= Time.deltaTime;
            if (_timer > 0f) return;

            // É só visual: cada máquina sorteia as próprias piscadas.
            bool next = !_lit;

            switch (_power)
            {
                case PowerPhase.Hurry:
                    // Últimos segundos: acende e apaga sem parar.
                    _timer = next ? Random.Range(0.05f, 0.16f) : Random.Range(0.04f, 0.11f);
                    break;

                case PowerPhase.Warning:
                    // Aviso: falha rápido e volta, uma ou duas vezes por segundo.
                    _timer = next ? Random.Range(0.3f, 1.1f) : Random.Range(0.05f, 0.14f);
                    break;

                default:
                    // Luminária defeituosa: fica acesa por períodos longos e apaga em rajadas curtas.
                    _timer = next ? Random.Range(0.08f, 2.5f) : Random.Range(0.03f, 0.22f);
                    break;
            }

            Apply(next);
        }

        void Apply(bool lit)
        {
            _lit = lit;

            // Apaga pelo dimmer em vez de desligar o componente: assim a sombra em cache
            // não precisa ser renderizada de novo a cada piscada.
            if (_lights != null)
                foreach (HDAdditionalLightData data in _lights)
                    if (data != null) data.lightDimmer = lit ? 1f : 0f;

            if (_panel != null) _panel.sharedMaterial = lit ? _onMaterial : _offMaterial;
        }
    }
}
