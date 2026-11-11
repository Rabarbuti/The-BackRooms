using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace BackRooms.UI
{
    /// <summary>
    /// Custom Pass do HDRP que aplica o shader BackRooms/CamcorderVHS em tela cheia.
    /// Depois do pós-processamento não dá para ler e escrever a imagem da câmera ao mesmo
    /// tempo, então ela é copiada para uma textura temporária e o shader lê dessa cópia.
    /// </summary>
    [System.Serializable]
    public sealed class CamcorderPass : CustomPass
    {
        public Material material;

        static readonly int SourceId = Shader.PropertyToID("_CamcorderSource");

        RTHandle _copy;

        protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            _copy = RTHandles.Alloc(
                Vector2.one, TextureXR.slices, dimension: TextureXR.dimension,
                colorFormat: GraphicsFormat.R16G16B16A16_SFloat,
                useDynamicScale: true, name: "Filmadora (cópia da imagem)");
        }

        protected override void Execute(CustomPassContext ctx)
        {
            if (material == null || _copy == null) return;

            CustomPassUtils.Copy(ctx, ctx.cameraColorBuffer, _copy);

            ctx.propertyBlock.SetTexture(SourceId, _copy);
            CoreUtils.SetRenderTarget(ctx.cmd, ctx.cameraColorBuffer, ClearFlag.None);
            CoreUtils.DrawFullScreen(ctx.cmd, material, ctx.propertyBlock, shaderPassId: 0);

            // A imagem está pronta, com o efeito e o painel: se o jogador está gravando, vai uma cópia para o vídeo.
            ScreenRecorder.Capture(ctx.hdCamera.camera, ctx.cmd, ctx.cameraColorBuffer);
        }

        protected override void Cleanup()
        {
            if (_copy != null) _copy.Release();
            _copy = null;
        }
    }
}
