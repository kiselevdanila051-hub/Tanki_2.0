// Небо карт Tanki X (TxMap): панорама 2 : 1 (равнопромежуточная, tx_export.py собирает её из кубической карты скайбокса
// карты): по горизонтали — долгота от −Z через +X (u 0,5 — направление +Z), по вертикали — высота (верх текстуры — зенит).
// _Rotation — поворот вокруг вертикали (градусы, как у Skybox/Cubemap), _Exposure и _Tint — как у материала TX (тинт 0,5 —
// без изменения). Мипы не читаются — на шве долготы без полосы.
Shader "Tanks/TxSky"
{
    Properties
    {
        _MainTex("Panorama", 2D) = "black" {}
        _Tint("Tint", Color) = (0.5, 0.5, 0.5, 1)
        _Exposure("Exposure", Float) = 1
        _Rotation("Rotation", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_linear_repeat);
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half _Exposure, _Rotation;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                float a = radians(_Rotation);
                float s = sin(a), c = cos(a);
                float3 p = input.positionOS.xyz;
                o.dir = float3(c * p.x - s * p.z, p.y, s * p.x + c * p.z);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 d = normalize(input.dir);
                float2 uv = float2(atan2(d.x, d.z) / (2.0 * PI) + 0.5, asin(clamp(d.y, -1.0, 1.0)) / PI + 0.5);
                half3 col = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_linear_repeat, uv, 0).rgb;
                return half4(col * _Tint.rgb * 2.0 * _Exposure, 1.0);
            }
            ENDHLSL
        }
    }
}
