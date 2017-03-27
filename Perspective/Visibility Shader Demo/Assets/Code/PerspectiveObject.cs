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

using UnityEngine;

namespace TinyRocketGames.Perspective.VisibilityShader
{
    [AddComponentMenu("Perspective/Visibility Shader/Perspective Object")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Renderer))]
    public class PerspectiveObject : MonoBehaviour
    {
        //This field is only serialised to expose the value in the Editor
        [SerializeField]
        private bool visible;
        public bool Visible { get { return visible; } internal set { visible = value; } }

        public bool IncludeChildren;
        internal Renderer[] childRenderers;

        internal Renderer attachedRenderer;

        void Awake()
        {
            attachedRenderer = GetComponent<Renderer>();
        }

        private void OnEnable()
        {
            PerspectiveController.Register(this);
            if (IncludeChildren)
            {
                //Note: Only goes one level down the hierarchy in this implementation. Can adapt to go further.
                childRenderers = GetComponentsInChildren<Renderer>();
            }
        }

        private void OnDisable()
        {
            PerspectiveController.Deregister(this);
        }

        private void OnDestroy()
        {
            var propBlock = new MaterialPropertyBlock();
            attachedRenderer.GetPropertyBlock(propBlock);
            propBlock.Clear();
            attachedRenderer.SetPropertyBlock(propBlock);
        }

        //This is an explicit/convenience method. Readable code is good.
        public bool CanBeSeenBy(PerspectiveCamera Camera)
        {
            return Camera.CanSee(this);
        }
    }
}
