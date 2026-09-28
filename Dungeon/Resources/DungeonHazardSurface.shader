Shader "GameTest/DungeonHazardSurface"
{
    Properties { _Color ("Color", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Color;
            struct Input { float4 vertex:POSITION; float3 normal:NORMAL; };
            struct Output { float4 position:SV_POSITION; float3 normal:TEXCOORD0; };
            Output vert(Input v) { Output o; o.position=UnityObjectToClipPos(v.vertex);o.normal=UnityObjectToWorldNormal(v.normal);return o; }
            fixed4 frag(Output i):SV_Target { float shade=.65+.35*saturate(dot(normalize(i.normal),normalize(float3(-.4,.8,-.5))));return fixed4(_Color.rgb*shade,1); }
            ENDCG
        }
    }
}
