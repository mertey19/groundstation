Shader "Simurgh/LiveMapPoints"
{
    Properties { _PointSize ("Point diameter in metres", Float) = 0.1 }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            Cull Off
            ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _PointSize;
            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            v2f vert(appdata v)
            {
                v2f o;
                float4 p = mul(UNITY_MATRIX_MV, v.vertex);
                p.xy += v.uv * _PointSize * 0.5;
                o.pos = mul(UNITY_MATRIX_P, p); o.color = v.color; o.uv = v.uv;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target { clip(1.0 - dot(i.uv, i.uv)); return i.color; }
            ENDCG
        }
    }
}
