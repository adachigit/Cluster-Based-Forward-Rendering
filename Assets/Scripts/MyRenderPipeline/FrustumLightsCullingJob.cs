using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using static Unity.Mathematics.math;
using float3 = Unity.Mathematics.float3;

namespace MyRenderPipeline
{
    public class FrustumLightsCullingJob : BaseRendererJob
    {
        private NativeArray<DataTypes.Frustum> viewFrustums;
        private NativeArray<DataTypes.Light> lights;
        private NativeMultiHashMap<int, short> frustumLightIndexes;
        private int lightsCount;
        private Array lightIndexList;

        private JobHandle jobHandle;
        
        private int2 screenDimension;
        
        private int gridSize;
        private int frustumsCount;
        private int frustumsCountVertical;
        private int frustumsCountHorizontal;

        private int maxLightsCount;
        private int maxLightsCountPerFrustum;
        private int jobBatchCount;
        
        public float4x4 InverseProjectionMat { get; set; }

        public FrustumLightsCullingJob(int jobBatchCount = 8)
        {
            this.jobBatchCount = jobBatchCount;
        }

        private void BuildViewFrustums()
        {
            if (viewFrustums.IsCreated)
            {
                viewFrustums.Dispose();
            }

            //Calculate frustums count in horizontal
            frustumsCountHorizontal = (screenDimension.x / gridSize);
            frustumsCountHorizontal += ((screenDimension.x % gridSize == 0) ? 0 : 1);
            //Calculate frustums count in vertical
            frustumsCountVertical = screenDimension.y / gridSize;
            frustumsCountVertical += ((0 == screenDimension.y % gridSize) ? 0 : 1);
            //Calculate total frustums count
            frustumsCount = frustumsCountHorizontal * frustumsCountVertical;

            viewFrustums = new NativeArray<DataTypes.Frustum>(frustumsCount, Allocator.Persistent);

            for (int i = 0; i < frustumsCountVertical; ++i)
            {
                for (int j = 0; j < frustumsCountHorizontal; ++j)
                {
                    viewFrustums[i * frustumsCountHorizontal + j] = BuildFrustum(int2(i, j));
                }
            }
        }

        private DataTypes.Frustum BuildFrustum(int2 frustumIndex)
        {
            /**
             * frustum四个坐标点的屏幕坐标
             * z为剪裁空间近剪裁面的z值，-1.0
             */
            float4[] screenPos = new float4[]
            {
                float4(frustumIndex.xy * gridSize, -1.0f, 1.0f),
                float4(float2(frustumIndex.x + 1, frustumIndex.y) * gridSize, -1.0f, 1.0f),
                float4(float2(frustumIndex.x, frustumIndex.y + 1) * gridSize, -1.0f, 1.0f),
                float4(float2(frustumIndex.x + 1, frustumIndex.y + 1) * gridSize, -1.0f, 1.0f),
            };

            float3[] viewPos = new float3[]
            {
                MathUtils.ScreenToView(screenPos[0], screenDimension, InverseProjectionMat).xyz,
                MathUtils.ScreenToView(screenPos[1], screenDimension, InverseProjectionMat).xyz,
                MathUtils.ScreenToView(screenPos[2], screenDimension, InverseProjectionMat).xyz,
                MathUtils.ScreenToView(screenPos[3], screenDimension, InverseProjectionMat).xyz,
            };
            
            float3 eye = float3.zero;

            DataTypes.Plane left = BuildPlane(eye, viewPos[2], viewPos[0]);
            
            return new DataTypes.Frustum
            {
                planeLeft = left,//BuildPlane(eye, viewPos[2], viewPos[0]),
                planeRight = BuildPlane(eye, viewPos[1], viewPos[3]),
                planeTop = BuildPlane(eye, viewPos[0], viewPos[1]),
                planeBottom = BuildPlane(eye, viewPos[3], viewPos[2]),
            };
        }
        
        /*
         * 计算一个平面
         * p0,p1,p2按逆时针解读，生成的平面法线方向遵从右手法则
         */
        private DataTypes.Plane BuildPlane(float3 p0, float3 p1, float3 p2)
        {
            float3 v1 = p1 - p0;
            float3 v2 = p2 - p0;
            float3 N = normalize(cross(v1, v2));

            return new DataTypes.Plane
            {
                normal = float4(N, 0.0f),
                distance = dot(N, p0),
            };
        }
        
        [BurstCompile(CompileSynchronously = true)]
        struct LightsCullingParallelFor : IJobParallelFor
        {
            [ReadOnly] public NativeArray<DataTypes.Light> lights;
            [ReadOnly] public NativeArray<DataTypes.Frustum> frustums;
            public NativeMultiHashMap<int, short>.ParallelWriter lightsCountAndIndexes;

            public int maxLightsCount;

            public void Execute(int index)
            {
                DataTypes.Frustum frustum = frustums[index];
                short lightCount = 0;

                for (short i = 0; i < lights.Length; ++i)
                {
                    var light = lights[i];
                    switch (light.type)
                    {
                        case EnumDef.LightType.Directional:
                            lightsCountAndIndexes.Add(index, i);
                            ++lightCount;
                            break;
                        case EnumDef.LightType.Point:
                            if (PointLightInFrustum(frustum, light))
                            {
                                lightsCountAndIndexes.Add(index, i);
                                ++lightCount;
                            }
                            break;
                        case EnumDef.LightType.Spot:
                            if (SpotLightInFrustum(frustum, light))
                            {
                                lightsCountAndIndexes.Add(index, i);
                                ++lightCount;
                            }
                            break;
                    }

                    if (lightCount >= maxLightsCount)
                        break;
                }

                lightsCountAndIndexes.Add(index, lightCount);    // Light index terminal flag
            }
            
            private bool PointLightInFrustum(DataTypes.Frustum frustum, DataTypes.Light light)
            {
                var sphere = new DataTypes.Sphere
                {
                    center = light.viewPos,
                    radius = light.range,
                };

                if (MathUtils.SphereBehindPlane(sphere, frustum.planeLeft))
                    return false;
                if (MathUtils.SphereBehindPlane(sphere, frustum.planeRight))
                    return false;
                if (MathUtils.SphereBehindPlane(sphere, frustum.planeTop))
                    return false;
                if (MathUtils.SphereBehindPlane(sphere, frustum.planeBottom))
                    return false;
            
                return true;
            }

            private bool SpotLightInFrustum(DataTypes.Frustum frustum, DataTypes.Light light)
            {
                
                var cone = new DataTypes.Cone
                {
                    pos = light.viewPos,
                    direction = light.viewDir,
                    height = light.range,
                    radius = light.radius
                };

                if (MathUtils.ConeBehindPlane(cone, frustum.planeLeft))
                    return false;
                if (MathUtils.ConeBehindPlane(cone, frustum.planeRight))
                    return false;
                if (MathUtils.ConeBehindPlane(cone, frustum.planeTop))
                    return false;
                if (MathUtils.ConeBehindPlane(cone, frustum.planeBottom))
                    return false;
                
                return true;
            }
        }

        private void CollectVisibleLights(Camera camera, CullingResults cullingResults)
        {
            if (lights.IsCreated)
            {
                lights.Dispose();
            }

            Matrix4x4 worldToViewMat = camera.worldToCameraMatrix;
            lightsCount = cullingResults.visibleLights.Length > maxLightsCount ? maxLightsCount : cullingResults.visibleLights.Length;
            lights = new NativeArray<DataTypes.Light>(lightsCount, Allocator.TempJob);

            for (int i = 0; i < lightsCount; ++i)
            {
                VisibleLight visibleLight = cullingResults.visibleLights[i];
                var light = new DataTypes.Light();

                switch (visibleLight.lightType)
                {
                case LightType.Directional:
                    light.type = EnumDef.LightType.Directional;
                    light.worldDir = visibleLight.localToWorldMatrix.GetColumn(2);
                    light.viewDir = mul(worldToViewMat, visibleLight.localToWorldMatrix.GetColumn(2));
                    light.color = visibleLight.finalColor;
                    break;
                case LightType.Point:
                    light.type = EnumDef.LightType.Point;
                    light.worldPos = visibleLight.localToWorldMatrix.GetColumn(3);
                    light.viewPos = mul(worldToViewMat, visibleLight.localToWorldMatrix.GetColumn(3));
                    light.range = visibleLight.range;
                    light.color = visibleLight.finalColor;
                    break;
                case LightType.Spot:
                    light.type = EnumDef.LightType.Spot;
                    light.worldPos = visibleLight.localToWorldMatrix.GetColumn(3);
                    light.viewPos = mul(worldToViewMat, visibleLight.localToWorldMatrix.GetColumn(3));
                    light.worldDir = visibleLight.localToWorldMatrix.GetColumn(2);
                    light.viewDir = mul(worldToViewMat, visibleLight.localToWorldMatrix.GetColumn(2));
                    light.range = visibleLight.range;
                    light.halfAngle = visibleLight.spotAngle * 0.5f;
                    light.radius = tan(radians(light.halfAngle));
                    light.color = visibleLight.finalColor;
                    break;
                default:
                    continue;
                }

                lights[i] = light;
            }
        }

        public override void Init(Camera camera, ScriptableRenderContext content)
        {
            NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace;
            
            MyRenderPipelineAsset pipelineAsset = GraphicsSettings.renderPipelineAsset as MyRenderPipelineAsset;
            var rendererData = pipelineAsset.GetRendererData<ForwardPlusRendererData>(MyRenderPipeline.RendererType.ForwardPlus);

            ForwardPlusCameraData cameraData = camera.GetComponent<ForwardPlusCameraData>();
            if(cameraData != null)
            {
                gridSize = cameraData.clusterGridBlockSize > 0 ? cameraData.clusterGridBlockSize : rendererData.clusterBlockGridSize;
                maxLightsCount = cameraData.maxLightsCount > 0 ? cameraData.maxLightsCount : rendererData.defaultMaxLightsCount;
                maxLightsCountPerFrustum = cameraData.maxLightsCountPerCluster > 0 ? cameraData.maxLightsCountPerCluster : rendererData.defaultMaxLightsCountPerCluster;
            }
            else
            {
                gridSize = rendererData.clusterBlockGridSize;
                maxLightsCount = rendererData.defaultMaxLightsCount;
                maxLightsCountPerFrustum = rendererData.defaultMaxLightsCountPerCluster;
            }
        }

        public override void BeforeRender(Camera camera, ScriptableRenderContext context, CullingResults cullingResults)
        {
            if (camera.scaledPixelWidth != screenDimension.x || camera.scaledPixelHeight != screenDimension.y)
            {
                screenDimension.x = camera.scaledPixelWidth;
                screenDimension.y = camera.scaledPixelHeight;

                InverseProjectionMat = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false).inverse;
                
                BuildViewFrustums();
                
                Debug.Log($"Rebuild frustums. GirdSize is {gridSize}, Frustums count is {frustumsCount}, viewFrustums size is {viewFrustums.Length}");
            }

            CollectVisibleLights(camera, cullingResults);
            
            frustumLightIndexes = new NativeMultiHashMap<int, short>(frustumsCount * (maxLightsCountPerFrustum + 1 /* Plus 1 for light count */), Allocator.TempJob);

            var job = new LightsCullingParallelFor
            {
                lights = lights,
                frustums = viewFrustums,
                lightsCountAndIndexes = frustumLightIndexes.AsParallelWriter(),
                maxLightsCount = 32,
            };
            jobHandle = job.Schedule(frustumsCount, jobBatchCount);
        }

        private void ReleaseResources()
        {
            if (lights.IsCreated)
            {
                lights.Dispose();
            }

            if (frustumLightIndexes.IsCreated)
            {
                frustumLightIndexes.Dispose();
            }
        }
        
        public override void AfterRender()
        {
            jobHandle.Complete();
            
            ReleaseResources();
        }

        public override void Dispose()
        {
            jobHandle.Complete();
            ReleaseResources();

            if (viewFrustums.IsCreated)
            {
                viewFrustums.Dispose();
            }
        }
    }
}