#ifndef _VOXEL_LIT_INC
#define _VOXEL_LIT_INC

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float3 viewDirWS : TEXCOORD2;
    float2 uv : TEXCOORD3;
    float4 shadowCoords : TEXCOORD4;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

void InitializeInputData(Varyings input, out InputData inputData)
{
    inputData = (InputData)0;

    #if defined(DEBUG_DISPLAY)
    inputData.positionWS = input.positionWS;
    inputData.positionCS = input.positionCS;
    inputData.viewDirectionWS = input.viewDirWS;
    #else
    inputData.positionWS = float3(0, 0, 0);
    inputData.viewDirectionWS = half3(0, 0, 1);
    #endif
}

Varyings vert(Attributes IN)
{
    Varyings OUT = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(IN);
    UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

    VertexPositionInputs positions = GetVertexPositionInputs(IN.positionOS.xyz);
    VertexNormalInputs normals = GetVertexNormalInputs(IN.normalOS);

    OUT.positionWS = positions.positionWS.xyz;
    OUT.normalWS = normals.normalWS.xyz;
    OUT.positionCS = positions.positionCS;
    OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
    OUT.shadowCoords = GetShadowCoord(positions);
    return OUT;
}

void frag(
    Varyings IN
    , out half4 outColor : SV_TARGET0
#ifdef _WRITE_RENDERING_LAYERS
    , out float4 outRenderingLayers : SV_TARGET1
#endif
)
{
    UNITY_SETUP_INSTANCE_ID(IN);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

    InputData inputData;
    InitializeInputData(IN, inputData);
    SETUP_DEBUG_TEXTURE_DATA(inputData, UNDO_TRANSFORM_TEX(IN.uv, _BaseMap));

    Light light = GetMainLight(IN.shadowCoords);
    float3 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
    float3 ambient = SampleSH(IN.normalWS);
    float3 lighting = LightingLambert(light.color, light.direction, IN.normalWS) * light.shadowAttenuation;
    float3 final = color * (lighting + ambient);
    outColor = float4(final, 1);

#ifdef _WRITE_RENDERING_LAYERS
    uint renderingLayers = GetMeshRenderingLayer();
    outRenderingLayers = float4(EncodeMeshRenderingLayer(renderingLayers), 0, 0, 0);
#endif
}


#endif