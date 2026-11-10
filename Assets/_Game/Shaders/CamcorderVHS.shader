// Efeito de filmadora VHS em tela cheia, aplicado depois do pós-processamento do HDRP
// por um Custom Pass (FullScreenCustomPass, ponto de injeção After Post Process).
//
// O que faz, na ordem:
//   1. sobrepõe o OSD da filmadora (REC, relógio, bateria) vindo de uma textura global;
//   2. desloca cada linha na horizontal (tremido, faixa de tracking, ruído de troca de
//      cabeça no pé do quadro);
//   3. separa brilho e cor e borra a cor na horizontal (cor "sangrando", típico de fita);
//   4. linhas de varredura, ruído, preto lavado, menos saturação;
//   5. escurece as bordas.
// Como o OSD entra antes da degradação, ele sofre os mesmos defeitos da imagem.
//
// Globais definidas pelo script Camcorder:
//   _BackRoomsOsd     textura do OSD (RGBA, alfa = cobertura)
//   _BackRoomsOsdOn   0 ou 1
//   _BackRoomsGlitch  0 a 1: interferência momentânea
Shader "BackRooms/CamcorderVHS"
{
    Properties
    {
        _Strength("Intensidade geral", Range(0, 1)) = 1
        _ChromaBleed("Cor sangrando (px)", Range(0, 8)) = 2.5
        _Scanlines("Linhas de varredura", Range(0, 1)) = 0.16
        _Noise("Ruído", Range(0, 1)) = 0.07
        _Wobble("Tremido horizontal (px)", Range(0, 6)) = 0.9
        _Tracking("Faixa de tracking", Range(0, 1)) = 0.22
        _HeadSwitch("Ruído no pé do quadro", Range(0, 1)) = 0.6
        _EdgeDark("Bordas escuras", Range(0, 1)) = 0.6
        _Desaturate("Menos saturação", Range(0, 1)) = 0.15
        _BlackLift("Preto lavado", Range(0, 0.2)) = 0.006
    }

    HLSLINCLUDE

    #pragma vertex Vert
    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2

    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"

    // Cópia da imagem da câmera, feita pelo CamcorderPass.
    TEXTURE2D_X(_CamcorderSource);
    TEXTURE2D(_BackRoomsOsd);
    float _BackRoomsOsdOn;
    float _BackRoomsGlitch;

    float _Strength;
    float _ChromaBleed;
    float _Scanlines;
    float _Noise;
    float _Wobble;
    float _Tracking;
    float _HeadSwitch;
    float _EdgeDark;
    float _Desaturate;
    float _BlackLift;

    float Hash21(float2 p)
    {
        p = frac(p * float2(123.34, 456.21));
        p += dot(p, p + 45.32);
        return frac(p.x * p.y);
    }

    // Imagem do jogo com o OSD por cima. Nos buffers do HDRP, uv.y = 0 é o pé do quadro.
    float3 Composite(float2 uv)
    {
        uv = saturate(uv);
        float3 scene = SAMPLE_TEXTURE2D_X_LOD(_CamcorderSource, s_linear_clamp_sampler, uv * _RTHandleScale.xy, 0).rgb;
        float4 osd = SAMPLE_TEXTURE2D_LOD(_BackRoomsOsd, s_point_clamp_sampler, uv, 0);
        return lerp(scene, osd.rgb, osd.a * _BackRoomsOsdOn);
    }

    float Luma(float3 c)
    {
        return dot(c, float3(0.299, 0.587, 0.114));
    }

    float4 FullScreenPass(Varyings varyings) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);

        float2 pixel = varyings.positionCS.xy;
        float2 uv = pixel * _ScreenSize.zw;
        float3 original = SAMPLE_TEXTURE2D_X_LOD(_CamcorderSource, s_linear_clamp_sampler, uv * _RTHandleScale.xy, 0).rgb;

        float t = _Time.y;
        float glitch = saturate(_BackRoomsGlitch);
        float texel = _ScreenSize.z;

        // A fita tem bem menos linhas que o monitor: agrupa as linhas da tela em ~270.
        float lineIndex = floor(uv.y * 270.0);
        float lineNoise = Hash21(float2(lineIndex, floor(t * 60.0)));

        // --- deslocamento horizontal por linha ---
        float shift = (lineNoise - 0.5) * _Wobble * (1.0 + 10.0 * glitch);

        // Faixa de tracking: uma tira que sobe devagar pelo quadro, rasgando a imagem.
        float bandCenter = frac(t * 0.06 + 0.37);
        float band = smoothstep(0.045, 0.0, abs(uv.y - bandCenter)) * (_Tracking + glitch);
        shift += band * (lineNoise - 0.5) * 34.0;

        // Troca de cabeça: as últimas linhas do quadro saem tortas e ruidosas.
        float foot = smoothstep(0.028, 0.0, uv.y) * _HeadSwitch;
        shift += foot * (12.0 + 46.0 * lineNoise);

        // Interferência forte: o quadro inteiro escorrega em blocos.
        float block = Hash21(float2(floor(uv.y * 14.0), floor(t * 18.0)));
        shift += glitch * step(0.6, block) * (block - 0.8) * 120.0;

        float2 uvD = float2(uv.x + shift * texel, uv.y);

        // --- brilho nítido, cor borrada e atrasada ---
        float bleed = _ChromaBleed * texel * (1.0 + 2.0 * glitch);
        float3 c0 = Composite(uvD);
        float3 cL1 = Composite(uvD - float2(bleed, 0));
        float3 cL2 = Composite(uvD - float2(bleed * 2.2, 0));
        float3 cL3 = Composite(uvD - float2(bleed * 3.6, 0));
        float3 cR1 = Composite(uvD + float2(bleed, 0));

        float luma = Luma(c0) * 0.6 + Luma(cL1) * 0.2 + Luma(cR1) * 0.2;
        float3 chroma = ((c0 - Luma(c0)) + (cL1 - Luma(cL1)) + (cL2 - Luma(cL2)) + (cL3 - Luma(cL3)) + (cR1 - Luma(cR1)) * 0.5) / 4.5;
        float3 color = luma + chroma;

        // --- linhas de varredura ---
        float scan = 0.5 - 0.5 * cos(uv.y * 270.0 * 6.28318);
        color *= 1.0 - _Scanlines * scan;

        // --- ruído ---
        float grain = Hash21(floor(pixel * 0.5) + frac(t * 7.13) * 311.7) - 0.5;
        color += grain * _Noise * (0.12 + luma);
        color += (band + foot) * (Hash21(float2(pixel.x * 0.25, lineIndex + t * 90.0)) - 0.5) * (0.06 + 0.3 * luma);

        // Chuvisco de interferência.
        float snow = Hash21(pixel * 0.37 + t * 53.0);
        color = lerp(color, float3(snow, snow, snow), glitch * 0.45);

        // --- tom de fita ---
        float gray = Luma(color);
        color = lerp(color, float3(gray, gray, gray), _Desaturate);
        color = color * (1.0 - _BlackLift) + _BlackLift;

        // --- bordas escuras ---
        float2 q = abs(uv - 0.5) * 2.0;
        float edge = smoothstep(0.55, 1.3, length(q * q));
        color *= 1.0 - _EdgeDark * edge;

        return float4(lerp(original, max(color, 0.0), _Strength), 1.0);
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }

        Pass
        {
            Name "Custom Pass 0"

            ZWrite Off
            ZTest Always
            Blend Off
            Cull Off

            HLSLPROGRAM
                #pragma fragment FullScreenPass
            ENDHLSL
        }
    }

    Fallback Off
}
