#ifndef _SHADER_COMMON_
#define _SHADER_COMMON_

float4x4 _InverseProjectionMatrix;
float4 ClusterCB_ScreenDimensions;

uint3 ClusterCB_GridDim;      // x, y, z 分别保存视锥体在x, y, z三个方向上被切分的个数，也就是各个分量上的子椎体个数
float ClusterCB_ViewNear;     // The distance to the near clipping plane. (Used for computing the index in the cluster grid)
uint2 ClusterCB_Size;         // 屏幕空间内以像素为单位的子椎体宽高
float ClusterCB_NearK;        // ( 1 + ( 2 * tan( fov * 0.5 ) / ClusterGridDim.y ) ) // Used to compute the near plane for clusters at depth k.
float ClusterCB_LogGridDimY;  // 1.0f / log( 1 + ( 2 * tan( fov * 0.5 ) / ClusterGridDim.y )

struct Plane
{
    float3 N;   // Plane normal.
    float  d;   // Distance to origin.
};

struct Cone
{
    float3  T;  // Cone tip
    float   h;  // Height of the cone
    float3  d;  // Direction of the cone
    float   r;  // bottom radius of the cone
};

struct AABB
{
    float4 Min;
    float4 Max;
//    float4 NLB;     // Point of Near-Left-Bottom
//    float4 FRT;     // Point of Far-Right-Top
};

//通过某个子椎体在所有子椎体中的索引值，计算它的三维索引值
//子椎体索引规则为从左到右，从上到下，从前到后
uint3 ComputeClusterIndex3D(uint clusterIndex1D)
{
    uint i = clusterIndex1D % ClusterCB_GridDim.x;
    uint j = clusterIndex1D % (ClusterCB_GridDim.x * ClusterCB_GridDim.y) / ClusterCB_GridDim.x;
    uint k = clusterIndex1D / (ClusterCB_GridDim.x * ClusterCB_GridDim.y);

    return uint3(i, j, k);
}

//上个函数的逆函数
uint ComputeClusterIndex1D(uint3 clusterIndex3D)
{
    return clusterIndex3D.x + ClusterCB_GridDim.x * (clusterIndex3D.y + ClusterCB_GridDim.y * clusterIndex3D.z);
}

//通过屏幕坐标和视空间下的Z坐标，计算子椎体的三维索引值
//计算公式：k = |log(Zvs / near) / log( 1 + ( 2 * tan( fov * 0.5 ) / ClusterGridDim.y )|
uint3 ComputeClusterIndex3D(float2 screenPos, float viewZ)
{
    uint i = screenPos.x / ClusterCB_Size.x;
    uint j = screenPos.y / ClusterCB_Size.y;
    uint k = log(viewZ / ClusterCB_ViewNear) * ClusterCB_LogGridDimY;

    return uint3(i, j, k);
}

//计算线段与平面相交
//参考《实时碰撞检测算法技术》5.3.1节
bool IntersectLinePlane(float3 a, float3 b, Plane p, out float3 q)
{
    float3 ab = b - a;

    float t = (p.d - dot(p.N, a)) / dot(p.N, ab);

    bool intersect = (t >= 0.0f && t <= 1.0f);

    q = float3(0, 0, 0);
//    if(intersect)     // 由于线段过短，必然不与平面相交，因此此处不应有此if判断
    {
        q = a + t * ab;
    }

    return intersect;
}

//剪裁空间到视空间的转化
float4 ClipToView(float4 clip)
{
    float4 view = mul(_InverseProjectionMatrix, clip);

    view = view / view.w;

    return view;
}

//屏幕空间到视空间的转化
float4 ScreenToView(float4 screen)
{
    float2 texCoord = screen.xy * ClusterCB_ScreenDimensions.zw;

    float4 clip = float4(texCoord * 2.0f - 1.0f, screen.z, screen.w);

    return ClipToView(clip);
}

float SqrDistancePointAABB(float3 p, AABB b)
{
    float sqDist = 0.0f;

    for(int i = 0; i < 3; ++i)
    {
        float v = p[i];
        if(v < b.Min[i]) sqDist += pow(b.Min[i] - v, 2);
        if(v > b.Max[i]) sqDist += pow(v - b.Max[i], 2);
    }

    return sqDist;
}

bool SphereInsideAABB(float3 center, float radius, AABB aabb)
{
    float sqrDistance = SqrDistancePointAABB(center, aabb);

    return sqrDistance <= pow(radius, 2);
}

Plane BuildPlane(float3 p0, float3 p1, float3 p2)
{
    Plane plane;

    float3 v1 = p1 - p0;
    float3 v2 = p2 - p0;

    plane.N = normalize(cross(v1, v2)); // cross function execute in left-hand rule.
    plane.d = dot(plane.N, p0);

    return plane;
}

bool PointInsidePlanePositiveSpace(float3 p, Plane plane)
{
    return dot(plane.N, p) - plane.d > 0;
}

bool ConeInsidePlane(Cone cone, Plane plane)
{
    float3 m = cross(cross(plane.N, cone.d), cone.d);
    float3 Q = cone.T + cone.d * cone.h + m * cone.r;

    return PointInsidePlanePositiveSpace(cone.T, plane) && PointInsidePlanePositiveSpace(Q, plane);
}

// Parameter planes contains the six planes of box.
// Each plane's normal must point the outer of box.
bool ConeInsideBoxPlanes(Cone cone, Plane planes[6])
{
    for(int i = 0; i < 6; ++i)
    {
        if(ConeInsidePlane(cone, planes[i]))
            return false;
    }

    return true;
}

#endif
