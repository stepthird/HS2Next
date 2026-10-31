// How a mosaic member sits in depth, shared by every pass of HS2Next/Mosaic and by the mask of the wide mosaic
// (HS2Next/Hidden/HS2MosaicWide), so the mask passes the camera depth exactly where the member won it.
// The member is drawn where it is (as HS2 draws its patches, 0.2-0.4 mm over the skin) with a constant-only polygon offset
// in every pass (Offset 0, -1: a tie with the surface under it goes to the member). No slope-scaled offset and no push
// along the normal: both grow the member's reach towards the camera and let it win against tight clothes seen from below
// (MosaicTest MR7: from below, BR-Chan's patch showed through her clothes on 5,598 px with Offset -1, -1 and on 5,665 px
// with a 0.4 mm push, on 2 px drawn as it is), while the skin under the patches never shows either way (MR7: 0 px).
#ifndef HS2NEXT_MOSAIC_DEPTH_INCLUDED
#define HS2NEXT_MOSAIC_DEPTH_INCLUDED

// The world position of a member vertex as every mosaic pass draws it.
float3 HS2MosaicPositionWS(float3 positionOS, float3 normalOS)
{
    return TransformObjectToWorld(positionOS);
}

#endif
