using Unity.Mathematics;
using static Unity.Mathematics.math;

namespace MyRenderPipeline
{
    public class MathUtils
    {
        /**
         * 通过屏幕空间坐标返回剪裁空间坐标，即将当前屏幕坐标转化到[-1.0, 1.0]范围内
         * screenDimension为屏幕分辨率
         * 只使用screen.xy两个分量，zw分量不做修改，直接放到返回值的相应字段中
         */
        public static float4 ScreenToClip(float4 screen, int2 screenDimension)
        {
            float2 texCoord = screen.xy / screenDimension;

            return float4(float2(texCoord.x, 1.0f - texCoord.y) * 2.0f - 1.0f, screen.z, screen.w);
        }

        /**
         * 剪裁空间坐标到视空间坐标的转换
         * clip.z需要存放[0-1]范围内的线性深度值
         * inverseProject为投影矩阵的逆矩阵
         */
        public static float4 ClipToView(float4 clip, float4x4 inverseProjection)
        {
            float4 view = mul(inverseProjection, clip);
            view = view / view.w;

            return view;
        }

        /**
         * 屏幕空间到视空间坐标的转换
         */
        public static float4 ScreenToView(float4 screen, int2 screenDimension, float4x4 inverseProjection)
        {
            var clip = ScreenToClip(screen, screenDimension);

            return ClipToView(clip, inverseProjection);
        }

        /**
         * 点是否在平面的背面（平面法线指向为平面正面）
         */
        public static bool PointBehindPlane(ref float3 point, ref DataTypes.Plane plane)
        {
            return dot(plane.normal.xyz, point) - plane.distance <= 0;
        }
        
        /**
         * 球体是否在平面的背面（平面法线指向为平面正面）
         */
        public static bool SphereBehindPlane(ref DataTypes.Sphere sphere, ref DataTypes.Plane plane)
        {
            return dot(plane.normal, sphere.center) - plane.distance < -sphere.radius;
        }

        /**
         * 球体是否在棱锥体内
         */
        public static bool SphereInsideFrustum(ref DataTypes.Sphere sphere, ref DataTypes.Frustum frustum)
        {
            if (SphereBehindPlane(ref sphere, ref frustum.planeLeft))
                return false;
            if (SphereBehindPlane(ref sphere, ref frustum.planeRight))
                return false;
            if (SphereBehindPlane(ref sphere, ref frustum.planeTop))
                return false;
            if (SphereBehindPlane(ref sphere, ref frustum.planeBottom))
                return false;

            return true;
        }
        
        /**
         * 球体是否与平面相交
         */
        public static bool SphereInsectPlane(DataTypes.Sphere sphere, DataTypes.Plane plane)
        {
            return abs(dot(plane.normal, sphere.center) - plane.distance) < sphere.radius;
        }

        /**
         * 圆锥体是否在平面的背面
         */
        public static bool ConeBehindPlane(ref DataTypes.Cone cone, ref DataTypes.Plane plane)
        {
            float3 pos = cone.pos.xyz;
            float3 m = cross(cross(plane.normal.xyz, cone.direction.xyz), cone.direction.xyz);
            float3 Q = cone.pos.xyz + cone.direction.xyz * cone.height + m * cone.radius;

            return PointBehindPlane(ref pos, ref plane) && PointBehindPlane(ref Q, ref plane);
        }

        public static bool ConeInsideFrustum(ref DataTypes.Cone cone, ref DataTypes.Frustum frustum)
        {
            if (ConeBehindPlane(ref cone, ref frustum.planeLeft))
                return false;
            if (ConeBehindPlane(ref cone, ref frustum.planeRight))
                return false;
            if (ConeBehindPlane(ref cone, ref frustum.planeTop))
                return false;
            if (ConeBehindPlane(ref cone, ref frustum.planeBottom))
                return false;
            
            return true;
        }
    }
}