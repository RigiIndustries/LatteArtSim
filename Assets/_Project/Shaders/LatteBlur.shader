Shader "Hidden/LatteBlur"
{
    Properties{ _MainTex("Base", 2D) = "white" {} }
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
            float4 _MainTex_TexelSize;
            float2 _Direction; // (1,0)=H, (0,1)=V

            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f { float2 uv:TEXCOORD0; float4 vertex:SV_POSITION; };

            v2f vert(appdata v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }

            float4 frag(v2f i) :SV_Target
            {
                float2 stepUV = _Direction * _MainTex_TexelSize.xy;
                // simple 3-tap, normalized
                float4 c0 = tex2D(_MainTex, i.uv - stepUV);
                float4 c1 = tex2D(_MainTex, i.uv);
                float4 c2 = tex2D(_MainTex, i.uv + stepUV);
                return (c0 + c1 + c2) * (1.0 / 3.0);
            }
            ENDHLSL
        }
    }
}
