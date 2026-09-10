Shader "Simurgh/SurveyModel"
{
    Properties { _Color ("Color", Color) = (0.2,0.35,0.3,1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            struct v2f { float4 pos:SV_POSITION; float3 normal:TEXCOORD0; };
            v2f vert(appdata_base v) { v2f o; o.pos=UnityObjectToClipPos(v.vertex); o.normal=UnityObjectToWorldNormal(v.normal); return o; }
            fixed4 frag(v2f i):SV_Target { float light=0.55+0.45*saturate(dot(normalize(i.normal),normalize(float3(-0.4,0.8,-0.3)))); return fixed4(_Color.rgb*light,1); }
            ENDCG
        }
    }
}
