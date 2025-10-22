Shader "Unlit/PointsColor"
{
    Properties {
        _PointSize("Point Size", Float) = 2.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            ZWrite On
            Cull Off

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _PointSize;

            struct appdata {
                float4 vertex : POSITION;
                float4 color  : COLOR;
            };

            struct v2f {
                float4 pos : SV_POSITION;
                float  ps  : PSIZE;
                fixed4 col : COLOR0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.ps  = _PointSize;
                o.col = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return i.col;
            }
            ENDCG
        }
    }
}
