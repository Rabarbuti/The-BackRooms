using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace BackRooms.World
{
    /// <summary>
    /// Uma luminária de teto: painel emissivo + luzes. Estados: ligada, piscando ou queimada.
    /// <see cref="SetPowered"/> existe para os eventos de fase (blackout) das próximas etapas.
    /// </summary>
    public sealed class CeilingLight : MonoBehaviour
    {
        HDAdditionalLightData[] _lights;
        MeshRenderer _panel;
        Material _onMaterial;
        Material _offMaterial;
        LightState _state;
        bool _lit;
        float _timer;

        public LightState State => _state;

        public void Init(LightState state, HDAdditionalLightData[] lights, MeshRenderer panel, Material onMaterial, Material offMaterial)
        {
            _state = state;
            _lights = lights;
            _panel = panel;
            _onMaterial = onMaterial;
            _offMaterial = offMaterial;

            Apply(state == LightState.On || state == LightState.Flicker);
            enabled = state == LightState.Flicker;
        }

        public void SetPowered(bool powered)
        {
            Apply(powered && (_state == LightState.On || _state == LightState.Flicker));
            enabled = powered && _state == LightState.Flicker;
        }

        void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;

            // Fica acesa por períodos longos e apaga em rajadas curtas.
            // É só visual: não precisa ser igual entre os clientes.
            bool next = !_lit;
            _timer = next ? Random.Range(0.08f, 2.5f) : Random.Range(0.03f, 0.22f);
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
