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

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TinyRocketGames.Perspective.VisibilityShader
{
    [AddComponentMenu("Perspective/Visibility Shader/Perspective Camera")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public class PerspectiveCamera : MonoBehaviour
    {
        [Tooltip("Stops updates to the set of Perspective Objects this camera can see")]
        public bool Frozen;

        internal Dictionary<PerspectiveObject, bool> visibleObjects;
        internal Camera attachedCamera;

        private void Awake()
        {
            attachedCamera = GetComponent<Camera>();
            visibleObjects = new Dictionary<PerspectiveObject, bool>();
        }

        private void OnEnable()
        {
            PerspectiveController.Register(this);
            attachedCamera.depthTextureMode = DepthTextureMode.Depth;
        }

        private void OnDisable()
        {
            attachedCamera.depthTextureMode = DepthTextureMode.None;
            PerspectiveController.Deregister(this);
        }

        /// <summary>
        /// Utilising the Visibility Shader mechanism, detect if the given PerspectiveObject is visible to this Camera.
        /// Note: Although pixel-perfect accuracy is given, the result may be a few frames behind what is rendered on-screen.
        /// </summary>
        /// <param name="Object"></param>
        /// <returns></returns>
        public bool CanSee(PerspectiveObject Object)
        {
            if (visibleObjects.ContainsKey(Object))
                return visibleObjects[Object];
            else
                return false;
        }

        /// <summary>
        /// Creates a snapshot of this PerspectiveCamera's current state including the PerspectiveObjects in view.
        /// </summary>
        /// <param name="UseCameraRenderTexture">
        /// Include a Texture2D screenshot using the properties of the Camera's currently-set RenderTexture
        /// <param name="ScreenshotTexture">
        /// Include a Texture2D screenshot using the properties of the provided RenderTexture</param>
        /// <returns></returns>
        public PerspectiveSnapshot TakeSnapshot(bool UseCameraRenderTexture = false, RenderTexture ScreenshotTexture = null)
        {
            PerspectiveObject[] a = visibleObjects.Where(x => x.Value == true).Select(x => x.Key).ToArray();

            if (!UseCameraRenderTexture && ScreenshotTexture != null)
            {
                var currentRT = RenderTexture.active;
                RenderTexture.active = ScreenshotTexture;
                var tex = new Texture2D(ScreenshotTexture.width, ScreenshotTexture.height);
                tex.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                RenderTexture.active = currentRT;
                return new PerspectiveSnapshot(this, a, tex);
            }
            else if(UseCameraRenderTexture && attachedCamera.targetTexture != null)
            {
                var currentRT = RenderTexture.active;
                RenderTexture.active = attachedCamera.targetTexture;
                var tex = new Texture2D(attachedCamera.targetTexture.width, attachedCamera.targetTexture.height);
                tex.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                RenderTexture.active = currentRT;
                return new PerspectiveSnapshot(this, a, tex);
            }
            else
                return new PerspectiveSnapshot(this, a, null);
        }
    }
}
