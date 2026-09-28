Shader "GameTest/AnimeToon"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Recolor ("Recolor texture luminance", Range(0,1)) = 0
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0.15
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull [_Cull]
        Pass
        {
            // An untagged pass is SRPDefaultUnlit in URP and also previews in Built-in.
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST, _Color;
            float _Recolor, _Cutoff;
            struct Input { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; };
            struct Output { float4 position:SV_POSITION; float2 uv:TEXCOORD0; float3 normal:TEXCOORD1; };
            Output vert(Input v)
            {
                Output o;
                o.position=UnityObjectToClipPos(v.vertex);
                o.uv=TRANSFORM_TEX(v.uv,_MainTex);
                o.normal=UnityObjectToWorldNormal(v.normal);
                return o;
            }
            fixed4 frag(Output i):SV_Target
            {
                fixed4 sample=tex2D(_MainTex,i.uv);
                clip(sample.a-_Cutoff);
                float brightness=dot(sample.rgb,float3(0.2126,0.7152,0.0722));
                float3 baseColor=lerp(sample.rgb,0.32+0.68*brightness,_Recolor)*_Color.rgb;
                float light=dot(normalize(i.normal),normalize(float3(-0.3,0.7,0.65)));
                float cel=lerp(0.8,1.0,smoothstep(-0.1,0.1,light));
                return fixed4(baseColor*cel,1);
            }
            ENDCG
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _Cutoff;
            struct Input { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct Output { float4 position:SV_POSITION; float2 uv:TEXCOORD0; };
            Output vert(Input v) { Output o; o.position=UnityObjectToClipPos(v.vertex); o.uv=v.uv; return o; }
            fixed4 frag(Output i):SV_Target { clip(tex2D(_MainTex,i.uv).a-_Cutoff); return 0; }
            ENDCG
        }
    }
}
