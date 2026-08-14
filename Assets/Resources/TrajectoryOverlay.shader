// Yorunge izleri icin "her seyin ustunde" cizen basit unlit shader.
//
// Neden gerekli: iz harita duzleminde sabit bir yukseklikte cizilir, ama Mapbox
// arazisi (yukseklik abartmasi ile) ve extrude edilen binalar bu duzlemin cok
// uzerine cikabilir; normal derinlik testiyle iz zeminin ALTINDA kalip tamamen
// gorunmez oluyordu. ZTest Always ile iz kamera ne gorursa gorsun ustte kalir.
//
// Resources klasorunde tutulur: yalnizca Shader.Find ile bulunan shader'lar
// bagimsiz surumde derlemeye dahil EDILMEZ; Resources.Load garanti eder.
Shader "Simurgh/TrajectoryOverlay"
{
    Properties
    {
        _Color ("Renk", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off
        Lighting Off
        Fog { Mode Off }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                fixed4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;   // LineRenderer renkleri vertex color ile gelir
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
