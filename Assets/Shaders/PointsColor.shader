Shader "Unlit/PointsColor"
{
    Properties {
        _PointSize("Point Size", Float) = 2.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100
        
        Pass
        {
            ZWrite On
            Cull Off

            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            // Meta Quest stereo rendering
            #pragma multi_compile _ STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON
            #include "UnityCG.cginc"

            float _PointSize;

            struct appdata {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f {
                float4 pos : SV_POSITION;
                float  ps  : PSIZE;
                fixed4 col : COLOR0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                
                o.pos = UnityObjectToClipPos(v.vertex);
                
                // Adaptive point size based on distance (critical for Quest performance)
                float dist = length(UnityObjectToViewPos(v.vertex));
                float sizeFactor = saturate(1.0 / (dist * 0.5)); // Scale down with distance
                o.ps = _PointSize * sizeFactor;
                
                o.col = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return i.col;
            }
            ENDCG
        }
    }
}
