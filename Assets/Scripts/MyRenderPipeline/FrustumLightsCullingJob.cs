using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using static Unity.Mathematics.math;
using float3 = Unity.Mathematics.float3;
using float4 = Unity.Mathematics.float4;

namespace MyRenderPipeline
{
    public class FrustumLightsCullingJob : BaseRendererJob
    {
        private NativeArray<DataTypes.Frustum> _viewFrustums;
        private NativeArray<DataTypes.Light> _lights;
        private NativeMultiHashMap<int, short> _frustumLightIndexes;
        private int _lightsCount;

        private readonly CommandBuffer _cmdBuffer = new CommandBuffer()
        {
            name = "FrustumLightsCulling",
        };

        private NativeArray<float4> _nativeLightBufferArray;
        private NativeArray<int4> _nativeIndexListBufferArray;
        
        private ComputeBuffer _cbLightBuffer;
        private ComputeBuffer _cbLightIndexListBuffer;
        
        private JobHandle _jobHandle;
        
        private int2 _screenDimension;
        
        private int _gridSize;
        private int _frustumsCount;
        private int _frustumsCountVertical;
        private int _frustumsCountHorizontal;

        private int _maxLightsCount;
        private int _maxLightsCountPerFrustum;
        private readonly int _jobBatchCount;
        
        public float4x4 inverseProjectionMat { get; set; }

        // For Debug Start
        // For Debug End
        
        public FrustumLightsCullingJob(int jobBatchCount = 8)
        {
            _jobBatchCount = jobBatchCount;
        }

        private void BuildViewFrustums()
        {
            if (_viewFrustums.IsCreated)
            {
                _viewFrustums.Dispose();
            }

            //Calculate frustums count in horizontal
            _frustumsCountHorizontal = (_screenDimension.x / _gridSize);
            _frustumsCountHorizontal += ((_screenDimension.x % _gridSize == 0) ? 0 : 1);
            //Calculate frustums count in vertical
            _frustumsCountVertical = _screenDimension.y / _gridSize;
            _frustumsCountVertical += ((0 == _screenDimension.y % _gridSize) ? 0 : 1);
            //Calculate total frustums count
            _frustumsCount = _frustumsCountHorizontal * _frustumsCountVertical;

            _viewFrustums = new NativeArray<DataTypes.Frustum>(_frustumsCount, Allocator.Persistent);

            for (int i = 0; i < _frustumsCountVertical; ++i)
            {
                for (int j = 0; j < _frustumsCountHorizontal; ++j)
                {
                    _viewFrustums[i * _frustumsCountHorizontal + j] = BuildFrustum(int2(j, i));
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
                float4(frustumIndex.xy * _gridSize, -1.0f, 1.0f),
                float4(float2(frustumIndex.x + 1, frustumIndex.y) * _gridSize, -1.0f, 1.0f),
                float4(float2(frustumIndex.x, frustumIndex.y + 1) * _gridSize, -1.0f, 1.0f),
                float4(float2(frustumIndex.x + 1, frustumIndex.y + 1) * _gridSize, -1.0f, 1.0f),
            };

            float3[] viewPos = new float3[]
            {
                MathUtils.ScreenToView(screenPos[0], _screenDimension, inverseProjectionMat).xyz,
                MathUtils.ScreenToView(screenPos[1], _screenDimension, inverseProjectionMat).xyz,
                MathUtils.ScreenToView(screenPos[2], _screenDimension, inverseProjectionMat).xyz,
                MathUtils.ScreenToView(screenPos[3], _screenDimension, inverseProjectionMat).xyz,
            };
            
            float3 eye = float3.zero;

            return new DataTypes.Frustum
            {
                planeLeft = BuildPlane(eye, viewPos[2], viewPos[0]),
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
            float3 n = normalize(cross(v1, v2));

            return new DataTypes.Plane
            {
                normal = float4(n, 0.0f),
                distance = dot(n, p0),
            };
        }
        
        private void CollectVisibleLights(Camera camera, CullingResults cullingResults)
        {
            if (_lights.IsCreated)
            {
                _lights.Dispose();
            }

            Matrix4x4 worldToViewMat = camera.worldToCameraMatrix;
            _lightsCount = cullingResults.visibleLights.Length > _maxLightsCount ? _maxLightsCount : cullingResults.visibleLights.Length;
            _lights = new NativeArray<DataTypes.Light>(_lightsCount, Allocator.TempJob);

            for (int i = 0; i < _lightsCount; ++i)
            {
                VisibleLight visibleLight = cullingResults.visibleLights[i];
                var light = new DataTypes.Light();

                switch (visibleLight.lightType)
                {
                case LightType.Directional:
                    light.type = EnumDef.LightType.Directional;
                    light.worldDir = -visibleLight.localToWorldMatrix.GetColumn(2);
                    light.viewDir = mul(worldToViewMat, -visibleLight.localToWorldMatrix.GetColumn(2));
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

                _lights[i] = light;
            }
        }

        [BurstCompile]
        struct LightsCollectJob : IJob
        {
            [ReadOnly]
            public NativeArray<VisibleLight> visibleLights;
            public NativeArray<DataTypes.Light> lights;
            public float4x4 worldToViewMat;
            public int lightsCount;
            
            public void Execute()
            {
                for (int i = 0; i < lightsCount; ++i)
                {
                    VisibleLight visibleLight = visibleLights[i];
                    var light = new DataTypes.Light();

                    switch (visibleLight.lightType)
                    {
                    case LightType.Directional:
                        light.type = EnumDef.LightType.Directional;
                        light.worldDir = -visibleLight.localToWorldMatrix.GetColumn(2);
                        light.viewDir = mul(worldToViewMat, -visibleLight.localToWorldMatrix.GetColumn(2));
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
        }
        
        [BurstCompile]
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
                            if (PointLightInFrustum(ref frustum, ref light))
                            {
                                lightsCountAndIndexes.Add(index, i);
                                ++lightCount;
                            }
                            break;
                        case EnumDef.LightType.Spot:
                            if (SpotLightInFrustum(ref frustum, ref light))
                            {
                                lightsCountAndIndexes.Add(index, i);
                                ++lightCount;
                            }
                            break;
                    }

                    if (lightCount >= maxLightsCount)
                        break;
                }
            }
            
            private bool PointLightInFrustum(ref DataTypes.Frustum frustum, ref DataTypes.Light light)
            {
                var sphere = new DataTypes.Sphere
                {
                    center = light.viewPos,
                    radius = light.range
                };
                
                return MathUtils.SphereInsideFrustum(ref sphere, ref frustum);
            }

            private bool SpotLightInFrustum(ref DataTypes.Frustum frustum, ref DataTypes.Light light)
            {
                var cone = new DataTypes.Cone
                {
                    pos = light.viewPos,
                    direction = light.viewDir,
                    height = light.range,
                    radius = light.radius
                };

                return MathUtils.ConeInsideFrustum(ref cone, ref frustum);
            }
        }

        public override void Init(Camera camera, ScriptableRenderContext content)
        {
            NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace;
            
            InitJobPersistentBuffers();
            InitConstantBuffers(_cmdBuffer);
        }

        private void ReloadRendererConfigure(Camera camera)
        {
            MyRenderPipelineAsset pipelineAsset = GraphicsSettings.renderPipelineAsset as MyRenderPipelineAsset;
            var rendererData = pipelineAsset.GetRendererData<ForwardPlusRendererData>(MyRenderPipeline.RendererType.ForwardPlus);

            ForwardPlusCameraData cameraData = camera.GetComponent<ForwardPlusCameraData>();
            if(cameraData != null)
            {
                _gridSize = cameraData.clusterGridBlockSize > 0 ? cameraData.clusterGridBlockSize : rendererData.clusterBlockGridSize;
                _maxLightsCount = cameraData.maxLightsCount > 0 ? cameraData.maxLightsCount : rendererData.defaultMaxLightsCount;
                _maxLightsCountPerFrustum = cameraData.maxLightsCountPerCluster > 0 ? cameraData.maxLightsCountPerCluster : rendererData.defaultMaxLightsCountPerCluster;
            }
            else
            {
                _gridSize = rendererData.clusterBlockGridSize;
                _maxLightsCount = rendererData.defaultMaxLightsCount;
                _maxLightsCountPerFrustum = rendererData.defaultMaxLightsCountPerCluster;
            }
        }
        
        public override void BeforeCulling(ref ScriptableCullingParameters param)
        {
            param.maximumVisibleLights = _maxLightsCount;
        }

        public override void BeforeRender(Camera camera, ScriptableRenderContext context, CullingResults cullingResults)
        {
            if (camera.scaledPixelWidth != _screenDimension.x || camera.scaledPixelHeight != _screenDimension.y)
            {
                _screenDimension.x = camera.scaledPixelWidth;
                _screenDimension.y = camera.scaledPixelHeight;

                inverseProjectionMat = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false).inverse;
                
                ReloadRendererConfigure(camera);
                BuildViewFrustums();
                
                _cmdBuffer.SetGlobalVector(ShaderIdsAndConstants.PropId_FrustumParamsId, new Vector4(_frustumsCountHorizontal, _frustumsCountVertical, _gridSize, _frustumsCount));
                
                context.ExecuteCommandBuffer(_cmdBuffer);
                _cmdBuffer.Clear();
                
                Debug.Log($"Rebuild frustums. GirdSize is {_gridSize}, Frustums count is {_frustumsCount}, viewFrustums size is {_viewFrustums.Length}");
            }

            if (_lights.IsCreated)
            {
                _lights.Dispose();
            }

            _lightsCount = cullingResults.visibleLights.Length > _maxLightsCount ? _maxLightsCount : cullingResults.visibleLights.Length;
            _lights = new NativeArray<DataTypes.Light>(_lightsCount, Allocator.TempJob);

            var lightsCollectJob = new LightsCollectJob
            {
                worldToViewMat = camera.worldToCameraMatrix,
                visibleLights = cullingResults.visibleLights,
                lightsCount = _lightsCount,
                lights = _lights,
            };
            var collectJobHandle = lightsCollectJob.Schedule();
            
            _frustumLightIndexes = new NativeMultiHashMap<int, short>(_frustumsCount * _maxLightsCountPerFrustum, Allocator.TempJob);

            var job = new LightsCullingParallelFor
            {
                lights = _lights,
                frustums = _viewFrustums,
                lightsCountAndIndexes = _frustumLightIndexes.AsParallelWriter(),
                maxLightsCount = _maxLightsCountPerFrustum,
            };
            _jobHandle = job.Schedule(_frustumsCount, _jobBatchCount, collectJobHandle);
        }

        private void InitJobPersistentBuffers()
        {
            _nativeLightBufferArray = new NativeArray<float4>(ShaderIdsAndConstants.ConstBuf_LightBuffer_EntriesCount, Allocator.Persistent);
            _nativeIndexListBufferArray = new NativeArray<int4>(ShaderIdsAndConstants.ConstBuf_LightIndexListBuffer_Entries_Count, Allocator.Persistent);
        }
        
        private unsafe void InitConstantBuffers(CommandBuffer cmdBuffer)
        {
            _cbLightBuffer = new ComputeBuffer(ShaderIdsAndConstants.ConstBuf_LightBuffer_EntriesCount, sizeof(float4), ComputeBufferType.Constant);
            _cbLightIndexListBuffer = new ComputeBuffer(ShaderIdsAndConstants.ConstBuf_LightIndexListBuffer_Entries_Count, sizeof(int4), ComputeBufferType.Constant);
        }

        private void ReleaseConstantBuffers()
        {
            if (_cbLightBuffer != null)
            {
                _cbLightBuffer.Release();
                _cbLightBuffer = null;
            }

            if (_cbLightIndexListBuffer != null)
            {
                _cbLightIndexListBuffer.Release();
                _cbLightIndexListBuffer = null;
            }
        }

        private void ReleaseJobPersistentBuffers()
        {
            if (_nativeLightBufferArray.IsCreated)
            {
                _nativeLightBufferArray.Dispose();
            }

            if (_nativeIndexListBufferArray.IsCreated)
            {
                _nativeIndexListBufferArray.Dispose();
            }
        }
        
        private void ReleaseJobTempBuffers()
        {
            if (_lights.IsCreated)
            {
                _lights.Dispose();
            }
            if (_frustumLightIndexes.IsCreated)
            {
                _frustumLightIndexes.Dispose();
            }
        }
        
        public override void AfterRender(Camera camera, ScriptableRenderContext context, CullingResults cullingResults)
        {
            _jobHandle.Complete();

//            GenerateLightsConstantBuffers(_cmdBuffer);
            
            context.ExecuteCommandBuffer(_cmdBuffer);
            _cmdBuffer.Clear();
            
            ReleaseJobTempBuffers();
        }

        private unsafe void GenerateLightsConstantBuffers(CommandBuffer cmdBuffer)
        {
            float4[] lightBuffer = new float4[ShaderIdsAndConstants.ConstBuf_LightBuffer_EntriesCount];
            int4[] lightIndexListBuffer = new int4[ShaderIdsAndConstants.MaxConstantBufferEntriesCount];

            for (int i = 0; i < _lights.Length; ++i)
            {
                DataTypes.Light light = _lights[i];
                if (light.type == EnumDef.LightType.Directional)
                {
                    lightBuffer[ShaderIdsAndConstants.PropOffset_LightDirectionsOrPositions + i] = light.worldDir;
                    lightBuffer[ShaderIdsAndConstants.PropOffset_LightAttenuations + i] = float4.zero;
                }
                else if (light.type == EnumDef.LightType.Point)
                {
                    lightBuffer[ShaderIdsAndConstants.PropOffset_LightDirectionsOrPositions + i] = light.worldPos;
                    lightBuffer[ShaderIdsAndConstants.PropOffset_LightAttenuations + i] = float4(1f / Mathf.Max(light.range * light.range, 0.00001f), 0.0f, 0.0f, 0.0f);
                }

                lightBuffer[ShaderIdsAndConstants.PropOffset_LightColors + i] = float4(light.color.r, light.color.g, light.color.b, light.color.a);
            }

            int currentListIndex = 0;
            
            for (int i = 0; i < _frustumsCount && i < ShaderIdsAndConstants.MaxFrustumsCount; ++i)
            {
                int gridStartIndex = currentListIndex;
                int gridLightsCount = 0;
                
                if (_frustumLightIndexes.TryGetFirstValue(i, out var lightIndex, out var it))
                {
                    do
                    {
                        lightIndexListBuffer[currentListIndex / 4][currentListIndex % 4] = lightIndex;
                        ++currentListIndex;
                        ++gridLightsCount;
                        
                        if (currentListIndex >= ShaderIdsAndConstants.ConstBuf_LightIndexListBuffer_Entries_Count * 4)
                            break;
                    } while (_frustumLightIndexes.TryGetNextValue(out lightIndex, ref it));
                }

                lightBuffer[ShaderIdsAndConstants.PropOffset_FrustumLightGrids + i] = int4(gridStartIndex, gridLightsCount, 0, 0);
                
                if (currentListIndex >= ShaderIdsAndConstants.MaxConstantBufferEntriesCount * 4)
                    break;
            }

            _cbLightBuffer.SetData(lightBuffer);
            _cbLightIndexListBuffer.SetData(lightIndexListBuffer);

            cmdBuffer.SetGlobalConstantBuffer(_cbLightBuffer, ShaderIdsAndConstants.ConstBufId_LightBufferId, 0, ShaderIdsAndConstants.ConstBuf_LightBuffer_Size);
            cmdBuffer.SetGlobalConstantBuffer(_cbLightIndexListBuffer, ShaderIdsAndConstants.ConstBufId_LightIndexListBufferId, 0, ShaderIdsAndConstants.ConstBuf_LightIndexListBuffer_Size);
            
            cmdBuffer.SetGlobalInt(ShaderIdsAndConstants.PropId_LightsCountId, _lightsCount);
        }

        [BurstCompile]
        struct GenerateLightsCBDatasJob : IJob
        {
            public NativeArray<float4> lightBufferArray;
            public NativeArray<int4> indexListBufferArray;

            public NativeArray<DataTypes.Light> lights;
            
            public void Execute()
            {
                
            }
        }
            
        public override void Dispose()
        {
            _jobHandle.Complete();

            ReleaseConstantBuffers();
            ReleaseJobPersistentBuffers();
            ReleaseJobTempBuffers();

            if (_viewFrustums.IsCreated)
            {
                _viewFrustums.Dispose();
            }
        }
    }
}