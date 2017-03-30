//  Copyright(c) 2017, Christopher J. Hill
//  All rights reserved.
//
//  Redistribution and use in source and binary forms, with or without modification,
//  are permitted provided that the following conditions are met:
//
//  1. Redistributions of source code must retain the above copyright notice,
//     this list of conditions and the following disclaimer.
//
//  2. Redistributions in binary form must reproduce the above copyright notice,
//     this list of conditions and the following disclaimer in the documentation
//     and/or other materials provided with the distribution.
//
//  3. Neither the name of the copyright holder nor the names of its contributors
//     may be used to endorse or promote products derived from this software without
//     specific prior written permission.
//
//  THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY
//  EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES
//  OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED.IN NO EVENT
//  SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
//  SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT
//  OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION)
//  HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR
//  TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE,
//  EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace TinyRocketGames.Perspective.VisibilityShader
{
    [DisallowMultipleComponent]
    public class PerspectiveController : MonoBehaviour
    {
        public const string SHADER_PERSPECTIVE_OBJECT_INDEX = "_PerspectiveObjIndex";
        public const string SHADER_CAMERA_IS_ACTIVE = "_CameraIsActive";
        public const string SHADER_CAMERA_INDEX_OFFSET = "_CameraIndexOffset";

        static List<PerspectiveCamera> perspectiveCameras = new List<PerspectiveCamera>();
        static Dictionary<Camera, int> associatedCamerasByIndex = new Dictionary<Camera, int>();
        static List<PerspectiveObject> perspectiveObjects = new List<PerspectiveObject>();

        public static ReadOnlyCollection<PerspectiveCamera> RegisteredCameras = new ReadOnlyCollection<PerspectiveCamera>(perspectiveCameras);
        public static ReadOnlyCollection<PerspectiveObject> RegisteredObjects = new ReadOnlyCollection<PerspectiveObject>(perspectiveObjects);

        static Queue<PerspectiveCamera> camerasToRegister = new Queue<PerspectiveCamera>();
        static Queue<PerspectiveCamera> camerasToDeregister = new Queue<PerspectiveCamera>();
        static Queue<PerspectiveObject> objectsToRegister = new Queue<PerspectiveObject>();
        static Queue<PerspectiveObject> objectsToDeregister = new Queue<PerspectiveObject>();
        static bool updateSets;

        bool cameraIsActive;

        ComputeBuffer computeBuffer = null;
        bool updateComputeBuffer;
        int computeBufferSize = 256;
        /// <summary>
        /// Changing the size of the ComputeBuffer is possible at runtime but not something you want to be doing every frame as it requires the creation of 
        /// a new ComputeBuffer which is a GPU-side resource.
        /// Resizing only occurs after the existing ComputeBuffer has been read or the PerspectiveController is re/enabled.
        /// Resizing happens prior to the de/registration of PerspectiveObjects and PerspectiveCameras.
        /// </summary>
        public int ComputeBufferSize
        {
            get { return computeBufferSize; }
            set { computeBufferSize = value; updateComputeBuffer = true; }
        }
        /// <summary>
        /// The buffer array. Ensure the struct-type's bit-representation matches that of the GPU.
        /// For example, bool is represented by a 16-bit struct on the GPU but an 8-bit struct on the CPU.
        /// To keep it simple and provide best compatibility with other API's we're using int for now.
        /// </summary>
        int[] computeBufferData;
        AsyncTextureReader.Status bufferRetrievalStatus;

#if UNITY_EDITOR
        //Serialised to allow modification in the Editor Inspector
        [Tooltip("The size of the ComputeBuffer used to get data from the GPU")]
        [SerializeField]
        int BufferSize = 256;
        private void OnValidate()
        {
            if(computeBufferSize != BufferSize)
            {
                BufferSize = computeBufferSize;
                ComputeBufferSize = computeBufferSize;
            }
        }
#endif

        public void OnEnable()
        { 
            if (computeBuffer == null)
            {
                if (SetComputeBuffer())
                    AsyncTextureReader.RequestBufferData(computeBuffer);
                else
                    return;
            }
            
            Camera.onPreRender += UpdateOnCameraPreRender;  
        }

        void OnDisable()
        {
            if (computeBuffer != null)
            {
                computeBuffer.Release();
                computeBuffer = null;
            }

            Camera.onPreRender -= UpdateOnCameraPreRender;
            Graphics.ClearRandomWriteTargets();
        }

        void Update()
        {
            //The AsyncTextureReader native plugin only supports DirectX at present so we'll simply go by platform until that changes
#if UNITY_STANDALONE_WIN
            GetComputeBufferWithAsyncPlugin();
#else
            GetComputeBuffer();
#endif
        }

        /// <summary>
        /// The main method using a Native Plugin for async buffer retrieval from the GPU.
        /// This should enable a more consitent frame-rate as the CPU does not need to block on the main thread waiting for data back from the GPU.
        /// Several additional frames of latency is the trade-off versus the synchronous method but the consistency could mitigate this if the framerate is high.
        /// The plugin currently supports DirectX 11 only but the source is available so developing for more targets is possible:
        /// https://github.com/SlightlyMad/AsyncTextureReader
        /// </summary>
        private void GetComputeBufferWithAsyncPlugin()
        {
            bufferRetrievalStatus = AsyncTextureReader.RetrieveBufferData(computeBuffer, computeBufferData);
            if (bufferRetrievalStatus == AsyncTextureReader.Status.Succeeded)
            {
                //Process the successfully retrieved data
                ProcessComputeBufferData();

                //Change the ComputeBuffer if a new buffer size has been set
                if (updateComputeBuffer)
                    SetComputeBuffer();

                //Update the sets with objects and cameras to be de/registered
                if (updateSets)
                    UpdateRegisteredSets();

                //Queue an async version of GetData() on the render-thread
                AsyncTextureReader.RequestBufferData(computeBuffer);

                //Queue an pre-emptive retrieve to copy the data from GPU to CPU (a possible 1-frame latency reduction but may cause issues on slower hardware. Testing required)
                AsyncTextureReader.RetrieveBufferData(computeBuffer, computeBufferData);

                //Queue a request to reset the computeBufferData
                ResetComputeBufferData();
            }
        }

        private void ProcessComputeBufferData()
        {
            //Work through the buffer to determine which PerspectiveObjects are visible by which PerspectiveCameras
            for (int objIdx = 0; objIdx < perspectiveObjects.Count; objIdx++)
            {
                PerspectiveObject currentObject = perspectiveObjects[objIdx];
                currentObject.Visible = false;

                for (int camIdx = 0; camIdx < perspectiveCameras.Count; camIdx++)
                {
                    PerspectiveCamera currentCamera = perspectiveCameras[camIdx];
                    if (currentCamera.Frozen)
                    {
                        foreach (var o in currentCamera.visibleObjects.Keys)
                            if (currentCamera.visibleObjects[o])
                                currentObject.Visible = true;
                    }
                    else
                    {
                        int cameraIdxOffset = camIdx * perspectiveObjects.Count;
                        if (computeBufferData[objIdx + cameraIdxOffset] > 0)
                        {
                            currentCamera.visibleObjects[currentObject] = true;
                            currentObject.Visible = true;
                        }
                        else
                            currentCamera.visibleObjects[currentObject] = false;
                    }
                }
            }
        }

        bool SetComputeBuffer()
        {
            if (!BufferThresholdTest())
                return false;

            if (computeBuffer != null)
            {
                computeBuffer.Release();
            }

            computeBuffer = new ComputeBuffer(computeBufferSize, sizeof(int));
            computeBufferData = new int[computeBufferSize];

            Graphics.SetRandomWriteTarget(4, computeBuffer, true);
            return true;
        }

        /// <summary>
        /// Processes Objects and Cameras queued to be de/registered.
        /// Designed around the current computebuffer implementation to keep the number of iterations to process the buffer as low as possible,
        /// achieved through the re/assignment of Object Id's to create the smallest distribution of values.
        /// This could be adapted to use a tag-based system with a DHCP-like reservation mechanism, allowing for more control over Id's and non-hierarchical Object-grouping.
        /// </summary>
        private void UpdateRegisteredSets()
        {
            //Deregister Objects
            while (objectsToDeregister.Count > 0)
            {
                var obj = objectsToDeregister.Dequeue();
                if (perspectiveObjects.Contains(obj))
                {
                    perspectiveObjects.Remove(obj);
                    foreach (var cam in perspectiveCameras)
                        cam.visibleObjects.Remove(obj);
                }
            }

            //Deregister Cameras
            while (camerasToDeregister.Count > 0)
            {
                var cam = camerasToDeregister.Dequeue();
                if (perspectiveCameras.Contains(cam))
                {
                    perspectiveCameras.Remove(cam);
                    associatedCamerasByIndex.Remove(cam.attachedCamera);
                }
            }

            //Register Objects
            while (objectsToRegister.Count > 0)
            {
                var obj = objectsToRegister.Dequeue();
                if (!perspectiveObjects.Contains(obj))
                {
                    perspectiveObjects.Add(obj);
                    if (!BufferThresholdTest(obj.gameObject))
                    {
                        perspectiveObjects.Remove(obj);
                        break;
                    }
                }
            }

            //Register Cameras
            while (camerasToRegister.Count > 0)
            {
                var cam = camerasToRegister.Dequeue();
                if (!perspectiveCameras.Contains(cam))
                {
                    perspectiveCameras.Add(cam);
                    if (!BufferThresholdTest(cam.gameObject))
                    {
                        perspectiveCameras.Remove(cam);
                        break;
                    }
                    associatedCamerasByIndex.Add(cam.attachedCamera, perspectiveCameras.IndexOf(cam));
                }
            }

            //Re/assign Object Id's in each Object's MaterialPropertyBlock
            for (int i = 0; i < perspectiveObjects.Count; i++)
            {
                var propBlock = new MaterialPropertyBlock();
                var obj = perspectiveObjects[i];
                obj.attachedRenderer.GetPropertyBlock(propBlock);
                propBlock.SetFloat(SHADER_PERSPECTIVE_OBJECT_INDEX, i);
                obj.attachedRenderer.SetPropertyBlock(propBlock);
                if (obj.IncludeChildren)
                {
                    foreach (var r in obj.childRenderers)
                    {
                        r.GetPropertyBlock(propBlock);
                        propBlock.SetFloat(SHADER_PERSPECTIVE_OBJECT_INDEX, i);
                        r.SetPropertyBlock(propBlock);
                    }
                }

                foreach (var cam in perspectiveCameras)
                    if (!cam.visibleObjects.ContainsKey(obj))
                        cam.visibleObjects.Add(obj, false);
            }

            updateSets = false;
        }

        private void ResetComputeBufferData()
        {
            //A simple zero-ing out of the computeBufferData array
            for (int i = 0; i < computeBufferData.Length; i++)
            {
                computeBufferData[i] = 0;
            }
            computeBuffer.SetData(computeBufferData);
        }

        /// <summary>
        /// Whenever ANY camera in the scene fires OnPreRender we look it up and change global Shader variables accordingly
        /// This also solves the issue of false-positives from the SceneView camera
        /// </summary>
        /// <param name="sender"></param>
        private void UpdateOnCameraPreRender(Camera sender)
        {
            if (associatedCamerasByIndex.ContainsKey(sender) && !perspectiveCameras[associatedCamerasByIndex[sender]].Frozen)
            {
                if (!cameraIsActive) //<-- This simple check prevents unnecessarily modifying the Shader variable, reducing GPU calls. Every little helps.
                {
                    Shader.SetGlobalInt(SHADER_CAMERA_IS_ACTIVE, 1);
                    cameraIsActive = true;
                }
                Shader.SetGlobalInt(SHADER_CAMERA_INDEX_OFFSET, associatedCamerasByIndex[sender] * perspectiveObjects.Count);
            }
            else
            {
                Shader.SetGlobalInt(SHADER_CAMERA_IS_ACTIVE, 0);
                cameraIsActive = false;
            }
        }

        //Don't want to overflow the computebuffer!
        private bool BufferThresholdTest(GameObject go = null)
        {
            if (perspectiveCameras.Count * perspectiveObjects.Count > computeBufferSize)
            {
                string msg = "PerspectiveController ComputeBufferSize exceeded threshold ";
                if (go != null)
                {
                    msg += "during the attempt to register \"" + go.name + "\" which was subsequently aborted";
                }
                else
                {
                    msg += "after being lowered at runtime. PerspectiveController disabled.";
                    enabled = false;
                }
                msg += ".\nThe product of PerspectiveObjects(" + perspectiveObjects.Count +
                    ") and PerspectiveCameras(" + perspectiveCameras.Count +
                    ") must not exceed the ComputeBufferSize(" + computeBufferSize + ").";

                Debug.LogError(msg);
                return false;
            }
            else
                return true;
        }

#region Static Register/Deregister Plumbing
        internal static void Register(PerspectiveCamera cam)
        {
            if (!camerasToRegister.Contains(cam))
            {
                camerasToRegister.Enqueue(cam);
                updateSets = true;
            }
        }

        internal static void Deregister(PerspectiveCamera cam)
        {
            if (!camerasToDeregister.Contains(cam))
            {
                camerasToDeregister.Enqueue(cam);
                updateSets = true;
            }
        }

        internal static void Register(PerspectiveObject obj)
        {
            if (!objectsToRegister.Contains(obj))
            {
                objectsToRegister.Enqueue(obj);
                updateSets = true;
            }
        }

        internal static void Deregister(PerspectiveObject obj)
        {
            if (!objectsToDeregister.Contains(obj))
            {
                objectsToDeregister.Enqueue(obj);
                updateSets = true;
            }
        }
#endregion

        //Old method to get data back from the GPU without the native plugin. Here in case we need to target non-DX11 builds although that needs conditional compilation setup
        private void GetComputeBuffer()
        {
            //The built-in Unity method is synchronous, can cause significant frame-time spikes when GPU and CPU are out of sync
            computeBuffer.GetData(computeBufferData);

            ProcessComputeBufferData();

            //Change the ComputeBuffer if a new buffer size has been set
            if (updateComputeBuffer)
                SetComputeBuffer();

            //Update the sets with objects and cameras to be de/registered
            if (updateSets)
                UpdateRegisteredSets();

            //Queue a request to reset the computeBufferData
            ResetComputeBufferData();
        }
    }
}

