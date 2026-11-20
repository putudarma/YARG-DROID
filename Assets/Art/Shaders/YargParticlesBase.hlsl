#ifndef YARG_PARTICLES_BASE_INCLUDED
#define YARG_PARTICLES_BASE_INCLUDED

// Define our custom implementations
half3 YargGetWorldSpaceNormalizeViewDir(float3 positionWS)
{
    // Single return point with an initialized local: the GLES/D3D shader
    // compilers flag early returns inside uniform-guarded branches as a
    // potentially uninitialized return value (build #4 failure).
    half3 viewDir = half3(0.0, 0.0, 1.0);
    if (_YargHighwaysN > 0)
    {
        float3 V = YargWorldSpaceCameraPos(positionWS).xyz - positionWS;
        viewDir = half3(normalize(V));
    }
    else if (IsPerspectiveProjection())
    {
        // Perspective
        float3 V = GetCurrentViewPosition() - positionWS;
        viewDir = half3(normalize(V));
    }
    else
    {
        // Orthographic
        viewDir = half3(-GetViewForwardDir());
    }
    return viewDir;
}

VertexPositionInputs YargGetVertexPositionInputs(float3 positionOS)
{
    VertexPositionInputs input;
    input.positionWS = TransformObjectToWorld(positionOS);
    input.positionVS = YargTransformWorldToView(input.positionWS);
    input.positionCS = YargTransformWorldToHClip(input.positionWS);

    float4 ndc = input.positionCS * 0.5f;
    input.positionNDC.xy = float2(ndc.x, ndc.y * _ProjectionParams.x) + ndc.w;
    input.positionNDC.zw = input.positionCS.zw;

    return input;
}

#endif // YARG_PARTICLES_BASE_INCLUDED
