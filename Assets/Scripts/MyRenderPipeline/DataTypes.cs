using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Mathematics;

namespace MyRenderPipeline
{
    public class DataTypes
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Plane
        {
            public float4 normal;
            public float distance;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Cone
        {
            public float4 pos;
            public float4 direction;
            public float height;
            public float radius;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Sphere
        {
            public float4 center;
            public float radius;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Frustum
        {
            public Plane planeLeft;
            public Plane planeRight;
            public Plane planeTop;
            public Plane planeBottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Light
        {
            public EnumDef.LightType type;
            public float range;
            public float halfAngle;        // for spot light
            public float radius;            // for spot light
            public Color color;
            public float4 worldPos;
            public float4 worldDir;
            public float4 viewPos;
            public float4 viewDir;
        }
    }
}
