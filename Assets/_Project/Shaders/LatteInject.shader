Shader "Hidden/LatteInject"
{
    Properties
    {
        _MainTex("ReadTex", 2D) = "black" {}
        _Center("Center", Vector) = (0.5, 0.5, 0, 0)
        _Radius("Radius", Float) = 0.1
        _Hardness("Hardness", Range(0,1)) = 0.5
        _Amount("Amount", Range(0,1)) = 1
    }
        SubShader
        {
            Tags { "RenderPipeline" = "UniversalRenderPipeline" "RenderType" = "Opaque" }
            ZWrite Off
            Cull Off
            ZTest Always

            Pass
            {
                HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment frag
                #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

                struct Attributes {
                    float4 positionOS : POSITION;
                    float2 uv         : TEXCOORD0;
                };
                struct Varyings {
                    float4 positionHCS : SV_POSITION;
                    float2 uv          : TEXCOORD0;
                };

                TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
                float4 _MainTex_ST;

                float2 _Center;
                float  _Radius;
                float  _Hardness;
                float  _Amount;

                Varyings vert(Attributes v)
                {
                    Varyings o;
                    o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                    o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                    return o;
                }

                float4 frag(Varyings i) : SV_Target
                {
                    float4 src = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);

                    float d = distance(i.uv, _Center);
                    float inner = _Radius * (1.0 - saturate(_Hardness));
                    float edge0 = inner;
                    float edge1 = _Radius;
                    float m = 1.0 - smoothstep(edge0, edge1, d);

                    float3 dst = lerp(src.rgb, 1.0.xxx, saturate(m * _Amount));
                    return float4(dst, 1.0);
                }
                ENDHLSL
            }
        }
}
