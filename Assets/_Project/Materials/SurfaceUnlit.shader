Shader "Unlit/NewUnlitShader"
{
    Properties
    {
        _MainTex ("Dye (RWTexture)", 2D) = "white" {}
        _CoffeeColor ("Coffee Color", Color) = (0.18, 0.10, 0.05, 1)
        _MilkColor   ("Milk Color",   Color) = (0.98, 0.95, 0.90, 1)
        _DepthStrength ("Depth Darkening", Range(0,1)) = 0.3
        _MilkCurve   ("Milk Curve", Range(0.2,3)) = 1.5
    }

    SubShader
    {
        Tags{"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline"}
        LOD 100

        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;

            float4 _CoffeeColor;
            float4 _MilkColor;
            float  _DepthStrength;
            float  _MilkCurve;

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                o.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv;
                float  dye = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).r;

                // Map dye -> milk amount (curved)
                float milk = saturate(dye);
                milk = pow(milk, _MilkCurve);

                float3 coffee = _CoffeeColor.rgb;
                float3 milkCol = _MilkColor.rgb;
                float3 baseCol = lerp(coffee, milkCol, milk);

                // radial depth darkening
                float2 center = float2(0.5, 0.5);
                float  r = distance(uv, center);
                float  depth = saturate(r / 0.5);
                float  depthTint = lerp(1.0, 1.0 - _DepthStrength, depth);

                // milk on top cancels some depth darkening
                depthTint = lerp(depthTint, 1.0, milk);
                baseCol *= depthTint;

                return float4(baseCol, 1.0);
            }
            ENDHLSL
        }
    }
}
