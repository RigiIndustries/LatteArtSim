Shader "Hidden/LatteDiffusionImplicit"
{
    Properties
    {
        _MainTex("Base (RGB)", 2D) = "white" {}
        _Lambda("Diffusion Lambda", Range(0,3)) = 0.5
        _Zero("Zero Threshold", Range(0,0.01)) = 0.0005
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
                #include "UnityCG.cginc"

                sampler2D _MainTex;
                float4    _MainTex_TexelSize;
                float     _Lambda;
                float     _Zero;

                struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
                struct v2f { float2 uv:TEXCOORD0; float4 vertex:SV_POSITION; };

                v2f vert(appdata v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }

                float4 frag(v2f i) : SV_Target
                {
                    float2 texel = _MainTex_TexelSize.xy;

                    // single-channel density for numerical stability
                    float c = tex2D(_MainTex, i.uv).r;
                    float l = tex2D(_MainTex, i.uv + float2(-texel.x, 0)).r;
                    float r = tex2D(_MainTex, i.uv + float2(texel.x, 0)).r;
                    float u = tex2D(_MainTex, i.uv + float2(0,  texel.y)).r;
                    float d = tex2D(_MainTex, i.uv + float2(0, -texel.y)).r;

                    // Jacobi implicit diffusion (stable)
                    float denom = 1.0 + 4.0 * _Lambda;
                    float xnew = (c + _Lambda * (l + r + u + d)) / denom;

                    // kill tiny speckle
                    if (xnew < _Zero) xnew = 0.0;

                    return float4(xnew, xnew, xnew, 1);
                }
                ENDHLSL
            }
        }
}
