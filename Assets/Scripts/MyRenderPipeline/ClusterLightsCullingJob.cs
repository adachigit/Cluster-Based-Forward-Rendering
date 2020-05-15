using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using static Unity.Mathematics.math;
using float3 = Unity.Mathematics.float3;
using float4 = Unity.Mathematics.float4;
using int3 = Unity.Mathematics.int3;

namespace MyRenderPipeline
{
    public class ClusterLightsCullingJob : BaseRendererJob
    {
        //persistent job data
        private NativeArray<DataTypes.Frustum> _viewClusters;
        //temp job data
        private NativeArray<DataTypes.Light> _lights;
        private NativeArray<int> _clusterLightsCount;
        private NativeMultiHashMap<int, short> _clusterLightIndexes;
        
        private readonly CommandBuffer _cmdBuffer = new CommandBuffer
        {
            name = "ClusterLightsCulling",
        };

        //constant buffer data
        private NativeArray<float4> _nativeLightBufferArray;
        private NativeArray<int4> _nativeClusterLightBufferArray;
        private ComputeBuffer _cbLightBuffer;
        private ComputeBuffer _cbClusterLightBuffer;
        
        private NativeArray<float4> _nativeIndexListBuffer1Array;
        private NativeArray<float4> _nativeIndexListBuffer2Array;
        private NativeArray<float4> _nativeIndexListBuffer3Array;
        private NativeArray<float4> _nativeIndexListBuffer4Array;
        private ComputeBuffer _cbLightIndexList1Buffer;
        private ComputeBuffer _cbLightIndexList2Buffer;
        private ComputeBuffer _cbLightIndexList3Buffer;
        private ComputeBuffer _cbLightIndexList4Buffer;
        
        private JobHandle _jobHandle;
        
        private int2 _screenDimension;
        private int _lightsCount;

        private float _cameraZNear;
        private float _cameraZFar;
        
        private int _gridSize;
        private int _clustersCount;
        private int _clustersCountX;
        private int _clustersCountY;
        private int _clustersCountZ;

        private float _clusterZFarMax;
        private float _clusterZStartStep;
        private float _clusterZStepRatio;

        private int _maxLightsCount;
        private int _maxLightsCountPerCluster;
        
        private readonly int _jobBatchCount;

        private float4x4 _inverseProjectionMat;

        public ClusterLightsCullingJob(int jobBatchCount = 200)
        {
            _jobBatchCount = jobBatchCount;
        }

        private void BuildViewClusters()
        {
            if (_viewClusters.IsCreated)
            {
                _viewClusters.Dispose();
            }

            _clustersCountX = _screenDimension.x / _gridSize;
            _clustersCountX = (_clustersCountX % _gridSize == 0 && _clustersCountX > 0) ? _clustersCountX : _clustersCountX + 1;
            _clustersCountY = _screenDimension.y / _gridSize;
            _clustersCountY = (_clustersCountY % _gridSize == 0 && _clustersCountY > 0) ? _clustersCountY : _clustersCountY + 1;
            
            _clustersCountZ = 1;
            _clustersCount = 0;

            while (_clustersCountZ * _clustersCountX * _clustersCountY <= ShaderIdsAndConstants.MaxClustersCount &&
                   GetClusterZFar(_clustersCountZ - 1) <= _clusterZFarMax && GetClusterZFar(_clustersCountZ - 1) <= _cameraZFar)
            {
                ++_clustersCountZ;
            }
            _clustersCount = _clustersCountX * _clustersCountY * _clustersCountZ;

            int3 clusterGridDim = int3(_clustersCountX, _clustersCountY, _clustersCountZ);
            _viewClusters = new NativeArray<DataTypes.Frustum>(_clustersCount, Allocator.Persistent);

            for (int i = 0; i < _clustersCount; ++i)
            {
                _viewClusters[i] = BuildCluster(MathUtils.ComputeClusterIndex3D(i, ref clusterGridDim));
            }
        }

        private float GetClusterZNear(int clusterZIndex)
        {
            //等比数列通项公式
            //Zn = Zn-1 + Ratio * Zn-1, (n >= 1), Z0 = ZStartStep，推导成通项公式
            //Zn = Z0 + Z0 * (1 - Ratio^n) / (1 - Ratio)
//            return _cameraZNear + _clusterZStartStep * (1.0f - pow(_clusterZStepRatio, clusterZIndex)) / (1.0f - _clusterZStepRatio);
            return _cameraZNear + pow(2, clusterZIndex);
        }

        private float GetClusterZFar(int clusterZIndex)
        {
            return GetClusterZNear(clusterZIndex + 1);
        }
        
        private DataTypes.Frustum BuildCluster(int3 clusterIndex3D)
        {
            /**
             * frustum四个坐标点的屏幕坐标
             * z为剪裁空间近剪裁面的z值，-1.0
             */
            float4[] screenPos =
            {
                float4(clusterIndex3D.xy * _gridSize, -1.0f, 1.0f),
                float4(float2(clusterIndex3D.x + 1, clusterIndex3D.y) * _gridSize, -1.0f, 1.0f),
                float4(float2(clusterIndex3D.x, clusterIndex3D.y + 1) * _gridSize, -1.0f, 1.0f),
                float4(float2(clusterIndex3D.x + 1, clusterIndex3D.y + 1) * _gridSize, -1.0f, 1.0f),
            };

            float3[] viewPos =
            {
                MathUtils.ScreenToView(screenPos[0], _screenDimension, _inverseProjectionMat).xyz,
                MathUtils.ScreenToView(screenPos[1], _screenDimension, _inverseProjectionMat).xyz,
                MathUtils.ScreenToView(screenPos[2], _screenDimension, _inverseProjectionMat).xyz,
                MathUtils.ScreenToView(screenPos[3], _screenDimension, _inverseProjectionMat).xyz,
            };
            
            float3 eye = float3.zero;

            return new DataTypes.Frustum
            {
                planeNear = new DataTypes.Plane { normal = float4(0f, 0f, -1f, 0f), distance = GetClusterZNear(clusterIndex3D.z) },
                planeFar = new DataTypes.Plane { normal = float4(0f, 0f, 1f, 0), distance = -GetClusterZFar(clusterIndex3D.z) },
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
            public NativeArray<int4> clusterLightBufferArray;
            public NativeArray<float4> indexListBuffer1Array;
            public NativeArray<float4> indexListBuffer2Array;
            public NativeArray<float4> indexListBuffer3Array;
            public NativeArray<float4> indexListBuffer4Array;

            [ReadOnly]
            public NativeArray<DataTypes.Light> lights;
            [ReadOnly]
            public NativeMultiHashMap<int, short> frustumLightIndexes;
            [ReadOnly]
            public NativeArray<int> frustumLightsCount;

            public int lightDirOrPosBufferOffset;
            public int lightAttenBufferOffset;
            public int lightColorsBufferOffset;
            public int indexListEntriesCount;

            private int _currentIndexArrayIndex;
            private int _currentLightIndexInArray;

            public void Execute()
            {
                _currentIndexArrayIndex = 0;
                _currentLightIndexInArray = 0;
                
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

                for (int i = 0; i < frustumLightsCount.Length; ++i)
                {
                    int lightCount = frustumLightsCount[i];
                    int indexArrayIndex = CheckLightIndexArrayIndex(lightCount);
                    if (indexArrayIndex >= ShaderIdsAndConstants.MaxLightIndexListCount)
                    {
                        clusterLightBufferArray[i] = int4(-1, 0, -1, 0);
                        continue;
                    }
                    else
                    {
                        clusterLightBufferArray[i] = int4(_currentLightIndexInArray, lightCount, indexArrayIndex, 0);
                    }

                    var ite = frustumLightIndexes.GetValuesForKey(i);
                    while (ite.MoveNext())
                    {
                        AddLightIndex(ite.Current);
                    }
                }
            }

            private int CheckLightIndexArrayIndex(int appendLightCount)
            {
                if (_currentLightIndexInArray + appendLightCount > indexListEntriesCount)
                {
                    ++_currentIndexArrayIndex;
                    _currentLightIndexInArray = 0;
                }

                return _currentIndexArrayIndex;
            }

            private void AddLightIndex(int lightIndex)
            {
                NativeArray<float4> tmpArray;

                switch (_currentIndexArrayIndex)
                { 
                case 0:
                    tmpArray = indexListBuffer1Array;
                    break;
                case 1:
                    tmpArray = indexListBuffer2Array;
                    break;
                case 2:
                    tmpArray = indexListBuffer3Array;
                    break;
                case 3:
                    tmpArray = indexListBuffer4Array;
                    break;
                default:
                    return;
                }
                
                float4 entry = tmpArray[_currentLightIndexInArray / 4];
                entry[_currentLightIndexInArray % 4] = lightIndex;
                tmpArray[_currentLightIndexInArray / 4] = entry;

                ++_currentLightIndexInArray;
            }
        }

        public override void Init(Camera camera, ScriptableRenderContext content)
        {
            NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace;
            
            InitConstantBuffers(_cmdBuffer);
        }

        public override void BeforeCulling(ref ScriptableCullingParameters param)
        {
            param.maximumVisibleLights = _maxLightsCount;
        }

        public override void BeforeRender(Camera camera, ScriptableRenderContext context, CullingResults cullingResults)
        {
            _jobHandle.Complete();
            RefreshConstantBuffer(context);
            
            context.ExecuteCommandBuffer(_cmdBuffer);
            _cmdBuffer.Clear();

            if (camera.scaledPixelWidth != _screenDimension.x || camera.scaledPixelHeight != _screenDimension.y)
            {
                _screenDimension.x = camera.scaledPixelWidth;
                _screenDimension.y = camera.scaledPixelHeight;
                _cameraZNear = camera.nearClipPlane;
                _cameraZFar = camera.farClipPlane;

                _inverseProjectionMat = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false).inverse;
                
                ReloadRendererConfigure(camera);
                BuildViewClusters();
                
                ReleaseJobNativeContainers();
                InitJobNativeContainers();
                
                Debug.Log($"Rebuild clusters. GirdSize is {_gridSize}, Clusters count is {_clustersCount}, x count is {_clustersCountX}, y count is {_clustersCountY}, z count is {_clustersCountZ}");
            }
            
            _lightsCount = cullingResults.visibleLights.Length;
            _clusterLightIndexes.Clear();

            var lightsCollectJob = new LightsCollectJob
            {
                worldToViewMat = camera.worldToCameraMatrix,
                visibleLights = cullingResults.visibleLights,
                lightsCount = _lightsCount,
                lights = _lights,
            };

            _jobHandle = lightsCollectJob.Schedule();
        }

        public override void AfterRender(Camera camera, ScriptableRenderContext context, CullingResults cullingResults)
        {
            _jobHandle.Complete();

            var lightsCullingJob = new LightsCullingParallelFor
            {
                lights = _lights,
                lightsCount = _lightsCount,
                frustums = _viewClusters,
                lightIndexes = _clusterLightIndexes.AsParallelWriter(),
                frustumLightCount = _clusterLightsCount,
                maxLightsCountPerFrustum = _maxLightsCountPerCluster,
            };
            var lightsCullingJobHandle = lightsCullingJob.Schedule(_clustersCount, _jobBatchCount);

            var generateCBJob = new GenerateLightsCBDatasJob
            {
                lightBufferArray = _nativeLightBufferArray,
                clusterLightBufferArray = _nativeClusterLightBufferArray,
                indexListBuffer1Array = _nativeIndexListBuffer1Array,
                indexListBuffer2Array = _nativeIndexListBuffer2Array,
                indexListBuffer3Array = _nativeIndexListBuffer3Array,
                indexListBuffer4Array = _nativeIndexListBuffer4Array,
                lights = _lights,
                frustumLightIndexes = _clusterLightIndexes,
                frustumLightsCount = _clusterLightsCount,

                lightDirOrPosBufferOffset = ShaderIdsAndConstants.PropOffset_LightDirectionsOrPositions,
                lightAttenBufferOffset = ShaderIdsAndConstants.PropOffset_LightAttenuations,
                lightColorsBufferOffset = ShaderIdsAndConstants.PropOffset_LightColors,
                indexListEntriesCount = ShaderIdsAndConstants.LightIndexList_Capacity,
            };
            _jobHandle = generateCBJob.Schedule(lightsCullingJobHandle);
            JobHandle.ScheduleBatchedJobs();
        }

        private unsafe void InitConstantBuffers(CommandBuffer cmdBuffer)
        {
            _cbLightBuffer = new ComputeBuffer(ShaderIdsAndConstants.ConstBuf_LightBuffer_EntriesCount, sizeof(float4), ComputeBufferType.Constant);
            _cbClusterLightBuffer = new ComputeBuffer(ShaderIdsAndConstants.ConstBuf_ClusterLightBuffer_EntriesCount, sizeof(int4), ComputeBufferType.Constant);
            _cbLightIndexList1Buffer = new ComputeBuffer(ShaderIdsAndConstants.ConstBuf_LightIndexListBuffer_Entries_Count, sizeof(float4), ComputeBufferType.Constant);
            _cbLightIndexList2Buffer = new ComputeBuffer(ShaderIdsAndConstants.ConstBuf_LightIndexListBuffer_Entries_Count, sizeof(float4), ComputeBufferType.Constant);
            _cbLightIndexList3Buffer = new ComputeBuffer(ShaderIdsAndConstants.ConstBuf_LightIndexListBuffer_Entries_Count, sizeof(float4), ComputeBufferType.Constant);
            _cbLightIndexList4Buffer = new ComputeBuffer(ShaderIdsAndConstants.ConstBuf_LightIndexListBuffer_Entries_Count, sizeof(float4), ComputeBufferType.Constant);
            
            _nativeLightBufferArray = new NativeArray<float4>(ShaderIdsAndConstants.ConstBuf_LightBuffer_EntriesCount, Allocator.Persistent);
            _nativeClusterLightBufferArray = new NativeArray<int4>(ShaderIdsAndConstants.ConstBuf_ClusterLightBuffer_EntriesCount, Allocator.Persistent);
            _nativeIndexListBuffer1Array = new NativeArray<float4>(ShaderIdsAndConstants.ConstBuf_LightIndexListBuffer_Entries_Count, Allocator.Persistent);
            _nativeIndexListBuffer2Array = new NativeArray<float4>(ShaderIdsAndConstants.ConstBuf_LightIndexListBuffer_Entries_Count, Allocator.Persistent);
            _nativeIndexListBuffer3Array = new NativeArray<float4>(ShaderIdsAndConstants.ConstBuf_LightIndexListBuffer_Entries_Count, Allocator.Persistent);
            _nativeIndexListBuffer4Array = new NativeArray<float4>(ShaderIdsAndConstants.ConstBuf_LightIndexListBuffer_Entries_Count, Allocator.Persistent);
        }

        private void ReloadRendererConfigure(Camera camera)
        {
            MyRenderPipelineAsset pipelineAsset = GraphicsSettings.renderPipelineAsset as MyRenderPipelineAsset;
            var rendererData = pipelineAsset.GetRendererData<ForwardPlusRendererData>(MyRenderPipeline.RendererType.ForwardPlus);

            ForwardPlusCameraData cameraData = camera.GetComponent<ForwardPlusCameraData>();
            if(cameraData != null)
            {
                _gridSize = cameraData.clusterGridSize > 0 ? cameraData.clusterGridSize : rendererData.clusterGridSize;
                _clusterZStartStep = cameraData.clusterZStartStep > 0.0f ? cameraData.clusterZStartStep : rendererData.clusterZStartStep;
                _clusterZStepRatio = cameraData.clusterZStepRatio > 0.0f ? cameraData.clusterZStepRatio : rendererData.clusterZStepRatio;
                _clusterZFarMax = cameraData.clusterZFarMax > 0.0f ? cameraData.clusterZFarMax : rendererData.clusterZFarMax;
                _maxLightsCount = cameraData.maxLightsCount > 0 ? cameraData.maxLightsCount : rendererData.maxLightsCount;
                _maxLightsCountPerCluster = cameraData.maxLightsCountPerCluster > 0 ? cameraData.maxLightsCountPerCluster : rendererData.maxLightsCountPerCluster;
            }
            else
            {
                _gridSize = rendererData.clusterGridSize;
                _clusterZStartStep = rendererData.clusterZStartStep;
                _clusterZStepRatio = rendererData.clusterZStepRatio;
                _clusterZFarMax = rendererData.clusterZFarMax;
                _maxLightsCount = rendererData.maxLightsCount;
                _maxLightsCountPerCluster = rendererData.maxLightsCountPerCluster;
            }

            var farClipPlane = camera.farClipPlane;
            _clusterZFarMax = _clusterZFarMax > farClipPlane ? farClipPlane : _clusterZFarMax;
        }

        private void ReleaseConstantBuffers()
        {
            if (_cbLightBuffer != null)
            {
                _cbLightBuffer.Release();
                _cbLightBuffer = null;
            }
            if (_cbClusterLightBuffer != null)
            {
                _cbClusterLightBuffer.Release();
                _cbClusterLightBuffer = null;
            }
            if (_cbLightIndexList1Buffer != null)
            {
                _cbLightIndexList1Buffer.Release();
                _cbLightIndexList1Buffer = null;
            }
            if (_cbLightIndexList2Buffer != null)
            {
                _cbLightIndexList2Buffer.Release();
                _cbLightIndexList2Buffer = null;
            }
            if (_cbLightIndexList3Buffer != null)
            {
                _cbLightIndexList3Buffer.Release();
                _cbLightIndexList3Buffer = null;
            }
            if (_cbLightIndexList4Buffer != null)
            {
                _cbLightIndexList4Buffer.Release();
                _cbLightIndexList4Buffer = null;
            }

            if (_nativeLightBufferArray.IsCreated)
            {
                _nativeLightBufferArray.Dispose();
            }
            if (_nativeClusterLightBufferArray.IsCreated)
            {
                _nativeClusterLightBufferArray.Dispose();
            }
            if (_nativeIndexListBuffer1Array.IsCreated)
            {
                _nativeIndexListBuffer1Array.Dispose();
            }
            if (_nativeIndexListBuffer2Array.IsCreated)
            {
                _nativeIndexListBuffer2Array.Dispose();
            }
            if (_nativeIndexListBuffer3Array.IsCreated)
            {
                _nativeIndexListBuffer3Array.Dispose();
            }
            if (_nativeIndexListBuffer4Array.IsCreated)
            {
                _nativeIndexListBuffer4Array.Dispose();
            }
        }
        
        private void RefreshConstantBuffer(ScriptableRenderContext context)
        {
            _cbLightBuffer.SetData(_nativeLightBufferArray);
            _cbClusterLightBuffer.SetData(_nativeClusterLightBufferArray);
            _cbLightIndexList1Buffer.SetData(_nativeIndexListBuffer1Array);
            _cbLightIndexList2Buffer.SetData(_nativeIndexListBuffer2Array);
            _cbLightIndexList3Buffer.SetData(_nativeIndexListBuffer3Array);
            _cbLightIndexList4Buffer.SetData(_nativeIndexListBuffer4Array);

            _cmdBuffer.SetGlobalConstantBuffer(_cbLightBuffer, ShaderIdsAndConstants.ConstBufId_LightBufferId, 0, ShaderIdsAndConstants.ConstBuf_LightBuffer_Size);
            _cmdBuffer.SetGlobalConstantBuffer(_cbClusterLightBuffer,ShaderIdsAndConstants.ConstBufId_ClusterLightBufferId, 0, ShaderIdsAndConstants.ConstBuf_ClusterLightBuffer_Size);
            _cmdBuffer.SetGlobalConstantBuffer(_cbLightIndexList1Buffer, ShaderIdsAndConstants.ConstBufId_LightIndexListBuffer1Id, 0, ShaderIdsAndConstants.ConstBuf_LightIndexListBuffer_Size);
            _cmdBuffer.SetGlobalConstantBuffer(_cbLightIndexList2Buffer, ShaderIdsAndConstants.ConstBufId_LightIndexListBuffer2Id, 0, ShaderIdsAndConstants.ConstBuf_LightIndexListBuffer_Size);
            _cmdBuffer.SetGlobalConstantBuffer(_cbLightIndexList3Buffer, ShaderIdsAndConstants.ConstBufId_LightIndexListBuffer3Id, 0, ShaderIdsAndConstants.ConstBuf_LightIndexListBuffer_Size);
            _cmdBuffer.SetGlobalConstantBuffer(_cbLightIndexList4Buffer, ShaderIdsAndConstants.ConstBufId_LightIndexListBuffer4Id, 0, ShaderIdsAndConstants.ConstBuf_LightIndexListBuffer_Size);

            _cmdBuffer.SetGlobalInt(ShaderIdsAndConstants.PropId_UseClusterCulling, 1);
            _cmdBuffer.SetGlobalFloat(ShaderIdsAndConstants.PropId_ClusterZStartStep, _clusterZStartStep);
            _cmdBuffer.SetGlobalFloat(ShaderIdsAndConstants.PropId_ClusterZStepRatio, _clusterZStepRatio);
            _cmdBuffer.SetGlobalInt(ShaderIdsAndConstants.PropId_LightsCountId, _lightsCount);
            _cmdBuffer.SetGlobalVector(ShaderIdsAndConstants.PropId_ClusterParamsId, new Vector4(_clustersCountX, _clustersCountY, _clustersCountZ, _gridSize));
        }
        
        private void InitJobNativeContainers()
        {
            _lights = new NativeArray<DataTypes.Light>(_maxLightsCount, Allocator.Persistent);
            _clusterLightIndexes = new NativeMultiHashMap<int, short>(_clustersCount * _maxLightsCountPerCluster, Allocator.Persistent);
            _clusterLightsCount = new NativeArray<int>(_clustersCount, Allocator.Persistent);
        }
        
        private void ReleaseJobNativeContainers()
        {
            if (_lights.IsCreated)
            {
                _lights.Dispose();
            }
            if (_clusterLightIndexes.IsCreated)
            {
                _clusterLightIndexes.Dispose();
            }
            if (_clusterLightsCount.IsCreated)
            {
                _clusterLightsCount.Dispose();
            }
        }
        
        public override void Dispose()
        {
            _jobHandle.Complete();

            ReleaseConstantBuffers();
            ReleaseJobNativeContainers();

            if (_viewClusters.IsCreated)
            {
                _viewClusters.Dispose();
            }
        }
    }
}