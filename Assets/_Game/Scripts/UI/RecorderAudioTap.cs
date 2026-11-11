using UnityEngine;

namespace BackRooms.UI
{
    /// <summary>
    /// Fica no mesmo objeto do ouvinte de áudio do jogador. O Unity entrega aqui, na thread de
    /// áudio, tudo o que vai sair para as caixas; o som passa adiante sem mudança e uma cópia
    /// vai para o <see cref="ScreenRecorder"/>, que só a aproveita enquanto está gravando.
    /// </summary>
    public sealed class RecorderAudioTap : MonoBehaviour
    {
        void OnAudioFilterRead(float[] data, int channels)
        {
            ScreenRecorder.PushAudio(data, channels);
        }
    }
}
