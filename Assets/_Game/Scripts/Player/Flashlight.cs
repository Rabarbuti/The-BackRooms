using UnityEngine;
using UnityEngine.InputSystem;

namespace BackRooms.Player
{
    /// <summary>
    /// Lanterna: liga com F (ou Y no controle), segue a câmera com um leve atraso e gasta bateria.
    /// Na lanterna de outro jogador <see cref="inputEnabled"/> fica desligado e o estado
    /// chega pela rede, por <see cref="SetOn"/>.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public sealed class Flashlight : MonoBehaviour
    {
        public Transform follow;
        public Vector3 offset = new Vector3(0.18f, -0.15f, 0.1f);
        [Tooltip("Quanto maior, mais rápido a lanterna alcança a câmera.")]
        public float followSharpness = 14f;
        [Tooltip("Só o jogador local lê o teclado.")]
        public bool inputEnabled = true;

        [Header("Bateria")]
        public bool infiniteBattery = true;
        public float batterySeconds = 600f;

        /// <summary>Bateria restante, de 0 a 1.</summary>
        public float Battery01 => batterySeconds <= 0f ? 0f : _battery / batterySeconds;
        public bool IsOn => _light != null && _light.enabled;

        /// <summary>O facho (luz spot). O Capitão confere se está dentro dele.</summary>
        public Light Beam => _light;

        Light _light;
        InputAction _toggle;
        float _battery;
        float _baseIntensity;

        void Awake()
        {
            _light = GetComponent<Light>();
            _baseIntensity = _light.intensity;
            _battery = batterySeconds;
            _light.enabled = false;

            _toggle = new InputAction("Flashlight", InputActionType.Button, "<Keyboard>/f");
            _toggle.AddBinding("<Gamepad>/buttonNorth");
        }

        void OnEnable() { _toggle.Enable(); }
        void OnDisable() { _toggle.Disable(); }
        void OnDestroy() { _toggle.Dispose(); }

        public void SetOn(bool on)
        {
            if (_light != null && _light.enabled != on) _light.enabled = on;
        }

        void Update()
        {
            if (inputEnabled && _toggle.WasPressedThisFrame() && (_battery > 0f || infiniteBattery))
                _light.enabled = !_light.enabled;

            if (!_light.enabled) return;

            // A bateria só é contada na lanterna do jogador local.
            if (inputEnabled && !infiniteBattery)
            {
                _battery = Mathf.Max(0f, _battery - Time.deltaTime);
                if (_battery <= 0f)
                {
                    _light.enabled = false;
                    return;
                }
            }

            // Nos últimos 10% a luz enfraquece e treme.
            float level = infiniteBattery || !inputEnabled ? 1f : Battery01;
            float dim = level > 0.1f ? 1f : Mathf.Lerp(0.35f, 1f, level / 0.1f) * Random.Range(0.8f, 1f);
            _light.intensity = _baseIntensity * dim;
        }

        void LateUpdate()
        {
            if (follow == null) return;

            float t = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            transform.position = follow.TransformPoint(offset);
            transform.rotation = Quaternion.Slerp(transform.rotation, follow.rotation, t);
        }

        public void Recharge(float seconds)
        {
            _battery = Mathf.Min(batterySeconds, _battery + seconds);
        }
    }
}
