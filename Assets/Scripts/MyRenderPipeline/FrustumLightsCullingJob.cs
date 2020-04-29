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
        //persistent job data
        private NativeArray<DataTypes.Frustum> _viewFrustums;
        //temp job data
        private NativeArray<DataTypes.Light> _lights;
        private NativeArray<int> _frustumLightsCount;
        private NativeMultiHashMap<int, short> _frustumLightIndexes;
        
        private readonly CommandBuffer _cmdBuffer = new CommandBuffer()
        {
            name = "FrustumLightsCulling",
        };

        //constant buffer data
        private NativeArray<float4> _nativeLightBufferArray;
        private NativeArray<int4> _nativeIndexListBufferArray;
        private ComputeBuffer _cbLightBuffer;
        private ComputeBuffer _cbLightIndexListBuffer;
        
        private JobHandle _jobHandle;
        
        private int2 _screenDimension;
        private int _lightsCount;

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

        [BurstCompile(CompileSynchronously = true)]
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
        
        [BurstCompile(CompileSynchronously = true)]
        struct LightsCullingParallelFor : IJobParallelFor
        {
            [ReadOnly] public NativeArray<DataTypes.Light> lights;
            [ReadOnly] public NativeArray<DataTypes.Frustum> frustums;
            public NativeMultiHashMap<int, short>.ParallelWriter lightIndexes;
            public NativeArray<int> frustumLightCount;

            public int lightsCount;
            public int maxLightsCountPerFrustum;

            public void Execute(int index)
            {
                DataTypes.Frustum frustum = frustums[index];
                short lightCount = 0;
                
                for (short i = 0; i < lightsCount; ++i)
                {
                    var light = lights[i];
                    switch (light.type)
                    {
                        case EnumDef.LightType.Directional:
                            lightIndexes.Add(index, i);
                            ++lightCount;
                            break;
                        case EnumDef.LightType.Point:
                            if (PointLightInFrustum(ref frustum, ref light))
                            {
                                lightIndexes.Add(index, i);
                                ++lightCount;
                            }
                            break;
                        case EnumDef.LightType.Spot:
                            if (SpotLightInFrustum(ref frustum, ref light))
                            {
                                lightIndexes.Add(index, i);
                                ++lightCount;
                            }
                            break;
                    }

                    if (lightCount >= maxLightsCountPerFrustum)
                        break;
                }

                frustumLightCount[index] = lightCount;
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

        [BurstCompile(CompileSynchronously = true)]
        struct GenerateLightsCBDatasJob : IJob
        {
            public NativeArray<float4> lightBufferArray;
            public NativeArray<int4> indexListBufferArray;

            [ReadOnly]
            public NativeArray<DataTypes.Light> lights;
            [ReadOnly]
            public NativeMultiHashMap<int, short> frustumLightIndexes;
            [ReadOnly]
            public NativeArray<int> frustumLightsCount;

            public int lightDirOrPosBufferOffset;
            public int lightAttenBufferOffset;
            public int lightColorsBufferOffset;
            public int frustumGridBufferOffset;
            public int indexListEntriesCount;

            public void Execute()
            {
                //Generate lights constant buffer
                for (int i = 0; i < lights.Length; ++i)
                {
                    DataTypes.Light light = lights[i];
                    if (light.type == EnumDef.LightType.Directional)
                    {
                        lightBufferArray[lightDirOrPosBufferOffset + i] = light.worldDir;
                        lightBufferArray[lightAttenBufferOffset + i] = float4.zero;
                    }
                    else if (light.type == EnumDef.LightType.Point)
                    {
                        lightBufferArray[lightDirOrPosBufferOffset + i] = light.worldPos;
                        lightBufferArray[lightAttenBufferOffset + i] = float4(1f / Mathf.Max(light.range * light.range, 0.00001f), 0.0f, 0.0f, 0.0f);
                    }

                    lightBufferArray[lightColorsBufferOffset + i] = float4(light.color.r, light.color.g, light.color.b, light.color.a);
                }

                int currentListIndex = 0;
                for (int i = 0; i < frustumLightsCount.Length; ++i)
                {
                    int lightCount = frustumLightsCount[i];
                    lightBufferArray[frustumGridBufferOffset + i] = int4(currentListIndex, lightCount, 0, 0);

                    var ite = frustumLightIndexes.GetValuesForKey(i);
                    while (ite.MoveNext())
                    {
                        int4 entry = indexListBufferArray[currentListIndex / 4];
                        entry[currentListIndex % 4] = ite.Current;
                        indexListBufferArray[currentListIndex / 4] = entry;

                        ++currentListIndex;
                        if (currentListIndex >= indexListEntriesCount * 4)
                            break;
                    }
                    
                    if (currentListIndex >= indexListEntriesCount * 4)
                        break;
                }
            }
        }
            
        public override void Init(Camera camera, ScriptableRenderContext content)
        {
            NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace;
            
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
                
                ReleaseJobNativeContainers();
                InitJobNativeContainers();
                
                _cmdBuffer.SetGlobalVector(ShaderIdsAndConstants.PropId_FrustumParamsId, new Vector4(_frustumsCountHorizontal, _frustumsCountVertical, _gridSize, _frustumsCount));
                
                context.ExecuteCommandBuffer(_cmdBuffer);
                _cmdBuffer.Clear();
                
                Debug.Log($"Rebuild frustums. GirdSize is {_gridSize}, Frustums count is {_frustumsCount}, viewFrustums size is {_viewFrustums.Length}");
            }

            _lightsCount = cullingResults.visibleLights.Length;
            _frustumLightIndexes.Clear();
            
            var lightsCollectJob = new LightsCollectJob
            {
                worldToViewMat = camera.worldToCameraMatrix,
                visibleLights = cullingResults.visibleLights,
                lightsCount = _lightsCount,
                lights = _lights,
            };
            var collectJobHandle = lightsCollectJob.Schedule();

            var lightsCullingJob = new LightsCullingParallelFor
            {
                lights = _lights,
                lightsCount = _lightsCount,
                frustums = _viewFrustums,
                lightIndexes = _frustumLightIndexes.AsParallelWriter(),
                frustumLightCount = _frustumLightsCount,
                maxLightsCountPerFrustum = _maxLightsCountPerFrustum,
            };
            var lightsCullingJobHandle = lightsCullingJob.Schedule(_frustumsCount, _jobBatchCount, collectJobHandle);

            var generateCBJob = new GenerateLightsCBDatasJob
            {
                lightBufferArray = _nativeLightBufferArray,
                indexListBufferArray = _nativeIndexListBufferArray,
                lights = _lights,
                frustumLightIndexes = _frustumLightIndexes,
                frustumLightsCount = _frustumLightsCount,
                
                lightDirOrPosBufferOffset = ShaderIdsAndConstants.PropOffset_LightDirectionsOrPositions,
                lightAttenBufferOffset = ShaderIdsAndConstants.PropOffset_LightAttenuations,
                lightColorsBufferOffset = ShaderIdsAndConstants.PropOffset_LightColors,
                frustumGridBufferOffset = ShaderIdsAndConstants.PropOffset_FrustumLightGrids,
                indexListEntriesCount = ShaderIdsAndConstants.ConstBuf_LightIndexListBuffer_Entries_Count,
            };
            _jobHandle = generateCBJob.Schedule(lightsCullingJobHandle);
        }

        public override void AfterRender(Camera camera, ScriptableRenderContext context, CullingResults cullingResults)
        {
            _jobHandle.Complete();

            _cbLightBuffer.SetData(_nativeLightBufferArray);
            _cbLightIndexListBuffer.SetData(_nativeIndexListBufferArray);

            _cmdBuffer.SetGlobalConstantBuffer(_cbLightBuffer, ShaderIdsAndConstants.ConstBufId_LightBufferId, 0, ShaderIdsAndConstants.ConstBuf_LightBuffer_Size);
            _cmdBuffer.SetGlobalConstantBuffer(_cbLightIndexListBuffer, ShaderIdsAndConstants.ConstBufId_LightIndexListBufferId, 0, ShaderIdsAndConstants.ConstBuf_LightIndexListBuffer_Size);
            
            _cmdBuffer.SetGlobalInt(ShaderIdsAndConstants.PropId_LightsCountId, _lightsCount);
            
            context.ExecuteCommandBuffer(_cmdBuffer);
            _cmdBuffer.Clear();
        }

        private unsafe void InitConstantBuffers(CommandBuffer cmdBuffer)
        {
            _cbLightBuffer = new ComputeBuffer(ShaderIdsAndConstants.ConstBuf_LightBuffer_EntriesCount, sizeof(float4), ComputeBufferType.Constant);
            _cbLightIndexListBuffer = new ComputeBuffer(ShaderIdsAndConstants.ConstBuf_LightIndexListBuffer_Entries_Count, sizeof(int4), ComputeBufferType.Constant);
            
            _nativeLightBufferArray = new NativeArray<float4>(ShaderIdsAndConstants.ConstBuf_LightBuffer_EntriesCount, Allocator.Persistent);
            _nativeIndexListBufferArray = new NativeArray<int4>(ShaderIdsAndConstants.ConstBuf_LightIndexListBuffer_Entries_Count, Allocator.Persistent);
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
            
            if (_nativeLightBufferArray.IsCreated)
            {
                _nativeLightBufferArray.Dispose();
            }
            if (_nativeIndexListBufferArray.IsCreated)
            {
                _nativeIndexListBufferArray.Dispose();
            }
        }

        private void InitJobNativeContainers()
        {
            _lights = new NativeArray<DataTypes.Light>(_maxLightsCount, Allocator.Persistent);
            _frustumLightIndexes = new NativeMultiHashMap<int, short>(_frustumsCount * _maxLightsCountPerFrustum, Allocator.Persistent);
            _frustumLightsCount = new NativeArray<int>(_frustumsCount, Allocator.Persistent);
        }
        
        private void ReleaseJobNativeContainers()
        {
            if (_lights.IsCreated)
            {
                _lights.Dispose();
            }
            if (_frustumLightIndexes.IsCreated)
            {
                _frustumLightIndexes.Dispose();
            }
            if (_frustumLightsCount.IsCreated)
            {
                _frustumLightsCount.Dispose();
            }
        }
        
        public override void Dispose()
        {
            _jobHandle.Complete();

            ReleaseConstantBuffers();
            ReleaseJobNativeContainers();

            if (_viewFrustums.IsCreated)
            {
                _viewFrustums.Dispose();
            }
        }
    }
}