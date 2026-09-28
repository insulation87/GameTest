Shader "GameTest/ItemSurface"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct Input { float4 vertex:POSITION; float3 normal:NORMAL; float4 color:COLOR; };
            struct Output { float4 position:SV_POSITION; float3 normal:TEXCOORD0; float4 color:COLOR; };
            Output vert(Input v)
            {
                Output o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.color = v.color;
                return o;
            }
            fixed4 frag(Output i):SV_Target
            {
                float light = dot(normalize(i.normal),normalize(float3(-.4,.8,-.5)));
                float shade = .68 + .32 * smoothstep(-.3,.8,light);
                return fixed4(i.color.rgb*shade,1);
            }
            ENDCG
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ColorMask 0
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 vertex:POSITION):SV_POSITION { return UnityObjectToClipPos(vertex); }
            fixed4 frag():SV_Target { return 0; }
            ENDCG
        }
    }
}
