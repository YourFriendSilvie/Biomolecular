// Biomolecular/VoxelTerrainLit — URP Opaque Triplanar Texture Array Shader
Shader "Biomolecular/VoxelTerrainLit"
{
    Properties
    {
        _TerrainTextures ("Terrain Textures Array", 2DArray) = "white" {}
        _TerrainTextureCount ("Terrain Texture Count", Float) = 1
        _AlbedoBoost ("Albedo Boost", Range(0.1, 4)) = 1.4
        _AmbientIntensity ("Ambient Intensity", Range(0, 0.5)) = 0.12
        _Exposure ("Exposure", Range(0.5, 3)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D_ARRAY(_TerrainTextures);
            SAMPLER(sampler_TerrainTextures);

            CBUFFER_START(UnityPerMaterial)
                float _TerrainTextureCount;
                float _AlbedoBoost;
                float _AmbientIntensity;
                float _Exposure;
            CBUFFER_END

            struct Attributes {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 uv0        : TEXCOORD0; // x: PrimaryID, y: SecondaryID, z: BlendWeight, w: slopeWeight
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings {
                float4 positionCS  : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float4 materialIDs : TEXCOORD2;
                float  fogFactor   : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes i) {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS  = TransformObjectToHClip(i.positionOS.xyz);
                o.positionWS  = TransformObjectToWorld(i.positionOS.xyz);
                o.normalWS    = TransformObjectToWorldNormal(i.normalOS);
                o.materialIDs = i.uv0; 
                o.fogFactor   = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half3 SampleTriplanar(float3 pos, float3 blend, float index) {
                half3 cx = SAMPLE_TEXTURE2D_ARRAY(_TerrainTextures, sampler_TerrainTextures, pos.zy, index).rgb;
                half3 cy = SAMPLE_TEXTURE2D_ARRAY(_TerrainTextures, sampler_TerrainTextures, pos.xz, index).rgb;
                half3 cz = SAMPLE_TEXTURE2D_ARRAY(_TerrainTextures, sampler_TerrainTextures, pos.xy, index).rgb;
                return cx * blend.x + cy * blend.y + cz * blend.z;
            }

            half4 Frag(Varyings i) : SV_Target {
                float3 normalWS = normalize(i.normalWS);
                float3 blend = pow(abs(normalWS), 10.0);
                blend /= dot(blend, 1.0);

                int layerCount = (int)max(1.0, _TerrainTextureCount);
                int idxA = (int)clamp(round(i.materialIDs.x), 0, layerCount - 1);
                int idxB = (int)clamp(round(i.materialIDs.y), 0, layerCount - 1);
                half3 colA = SampleTriplanar(i.positionWS, blend, idxA);
                half3 colB = SampleTriplanar(i.positionWS, blend, idxB);
                half3 albedo = lerp(colA, colB, i.materialIDs.z) * _AlbedoBoost;

                // Slope-aware rock blending: use slopeWeight (packed in materialIDs.w) to bias toward rock-like appearance on steep faces
                float slopeWeight = saturate(i.materialIDs.w);
                // Build a simple rock tint by desaturating and darkening the albedo
                half3 rockAlbedo = lerp(albedo, half3(0.45,0.45,0.5) * _AlbedoBoost, 0.8);
                albedo = lerp(albedo, rockAlbedo, slopeWeight);

                Light mainLight = GetMainLight();
                half3 diffuse = albedo * (mainLight.color * mainLight.shadowAttenuation * saturate(dot(normalWS, mainLight.direction)) + _AmbientIntensity);
                return half4(MixFog(diffuse * _Exposure, i.fogFactor), 1.0);
            }
            ENDHLSL
        }
    }
}