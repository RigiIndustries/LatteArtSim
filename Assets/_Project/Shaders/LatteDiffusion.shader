Shader "Hidden/LatteDiffusion"
{
    Properties
    {
        _MainTex("Base (RGB)", 2D) = "white" {}
        _Diffusion("Diffusion Rate", Range(0,200000)) = 60000
    }
        SubShader
        {
            Tags { "RenderType" = "Opaque" }
            Cull Off ZWrite Off ZTest Always

            Pass
            {
                HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment frag
                #pragma multi_compile __ DIFF_SOLID DIFF_LAPLACE
                #include "UnityCG.cginc"

                sampler2D _MainTex;
                float4 _MainTex_TexelSize;
                float  _Diffusion;

                struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
                struct v2f { float2 uv:TEXCOORD0; float4 vertex:SV_POSITION; };

                v2f vert(appdata v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }

                float4 frag(v2f i) : SV_Target
                {
                    // --- DEBUG: show green if the pass is running at all ---
                    #if defined(DIFF_SOLID)
                        return float4(0,1,0,1);
                    #endif

                    float2 texel = _MainTex_TexelSize.xy;

                    float4 c = tex2D(_MainTex, i.uv);
                    float4 l = tex2D(_MainTex, i.uv + float2(-texel.x, 0));
                    float4 r = tex2D(_MainTex, i.uv + float2(texel.x, 0));
                    float4 u = tex2D(_MainTex, i.uv + float2(0,  texel.y));
                    float4 d = tex2D(_MainTex, i.uv + float2(0, -texel.y));

                    float4 lap = (l + r + u + d - 4.0 * c);

                    // --- DEBUG: visualize Laplacian (edges) ---
                    #if defined(DIFF_LAPLACE)
                        // normalize lap to 0..1 for viewing
                        float4 vis = 0.5 + 2.0 * lap;  // boost for visibility
                        return saturate(vis);
                    #endif

                        // Resolution-independent diffusion: multiply by pixel size^2
                        float scale = texel.x * texel.x; // assume square pixels
                        float4 result = c - _Diffusion * scale * 0.25 * lap;

                        return saturate(result);
                    }
                    ENDHLSL
                }
        }
}
