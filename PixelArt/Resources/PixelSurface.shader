Shader "GameTest/PixelSurface"
{
    Properties
    {
        _Color ("Palette Color", Color) = (0.42,0.53,0.64,1)
        _MainTex ("Original Texture", 2D) = "white" {}
        _TexturePixels ("Texture Grid", Float) = 32
        _WorldPattern ("Stone Pattern", Float) = 0
        _TextureInfluence ("Texture Contrast", Range(0,1)) = 1
        _TextureSaturation ("Texture Saturation", Range(0,1)) = 1
        _MinimumShade ("Shadow Readability", Range(0.4,1)) = 0.6
        _MortarContrast ("Mortar Contrast", Range(0,0.6)) = 0.32
        _GrainStrength ("Surface Grain", Range(0,1)) = 1
        _ToneSteps ("Palette Steps", Range(15,128)) = 15
    }
    CGINCLUDE
    int _FloorCutoutCount;
    float4 _FloorCutoutCenters[32], _FloorCutoutAxesX[32], _FloorCutoutAxesZ[32];
    void ClipFloorOpenings(float3 world)
    {
        [loop] for (int cut = 0; cut < min(_FloorCutoutCount,32); cut++)
        {
            float3 delta = world - _FloorCutoutCenters[cut].xyz;
            if (abs(delta.y) < _FloorCutoutCenters[cut].w &&
                abs(dot(delta,_FloorCutoutAxesX[cut].xyz)) < _FloorCutoutAxesX[cut].w &&
                abs(dot(delta,_FloorCutoutAxesZ[cut].xyz)) < _FloorCutoutAxesZ[cut].w) clip(-1);
        }
    }
    ENDCG
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST, _Color;
            float _TexturePixels, _WorldPattern;
            float _TextureInfluence, _TextureSaturation, _MinimumShade, _MortarContrast, _GrainStrength, _ToneSteps;
            struct Input { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; };
            struct Output { float4 position:SV_POSITION; float3 normal:TEXCOORD0; float2 uv:TEXCOORD1; float3 world:TEXCOORD2; };
            Output vert(Input v)
            {
                Output o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }
            fixed4 frag(Output i):SV_Target
            {
                ClipFloorOpenings(i.world);
                float3 n = normalize(i.normal);
                float lighting = dot(n, normalize(float3(-.4,.8,-.5)));
                float shade = lighting > .55 ? 1.0 : lighting > -.1 ? (1.0 + _MinimumShade) * .5 : _MinimumShade;
                float2 uv = (floor(i.uv * _TexturePixels) + .5) / _TexturePixels;
                float3 tex = tex2Dlod(_MainTex, float4(uv,0,0)).rgb;
                float luminance = dot(tex,float3(.2126,.7152,.0722));
                tex = lerp(luminance.xxx,tex,_TextureSaturation);
                float steps = max(15,_ToneSteps);
                float3 tint = floor(saturate(lerp(float3(1,1,1),tex,_TextureInfluence) * _Color.rgb) * steps + .5) / steps;
                if (_WorldPattern > .5)
                {
                    float3 a = abs(n);
                    float2 p = a.y > max(a.x,a.z) ? i.world.xz : a.x > a.z ? i.world.zy : i.world.xy;
                    p = floor(p * 16) / 16;
                    float row = floor(p.y * 2);
                    float2 brick = float2(p.x + fmod(abs(row),2) * .5, p.y * 2);
                    float2 cell = frac(brick);
                    float mortar = cell.x < .0625 || cell.y < .125 ? 1 - _MortarContrast : 1;
                    float grain = frac(sin(dot(floor(p*16), float2(12.9898,78.233))) * 43758.5453);
                    tint *= mortar * lerp(1,grain > .84 ? .85 : grain < .12 ? 1.08 : 1,_GrainStrength);
                }
                return fixed4(floor(saturate(tint * shade) * steps + .5) / steps, 1);
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
            #pragma target 3.0
            #pragma multi_compile_shadowcaster
            #include "UnityCG.cginc"
            struct Output { V2F_SHADOW_CASTER; float3 world:TEXCOORD3; };
            Output vert(appdata_base v) { Output o; TRANSFER_SHADOW_CASTER_NORMALOFFSET(o) o.world=mul(unity_ObjectToWorld,v.vertex).xyz;return o; }
            float4 frag(Output i):SV_Target { ClipFloorOpenings(i.world); SHADOW_CASTER_FRAGMENT(i) }
            ENDCG
        }
    }
}
