Shader "Perspective/Visibility Tester"
{
	Properties
	{
		[Toggle(CLIP_ON)] _Clip("Clip Output", Int) = 0
		//The secret source. Set per-renderer/material instance using MaterialPropertyBlock
		[PerRendererData]_PerspectiveObjIndex("Perspective Object Index", int) = 0
	}

	SubShader
	{
		Tags{ "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True"}
		
		Cull Off
		Pass
		{
			Name "VISIBILITY TESTER"
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma fragmentoption ARB_precision_hint_nicest
			#pragma target 5.0
			#pragma shader_feature CLIP_ON
			#include "UnityCG.cginc"

			//UAV ComputeBuffer
			RWStructuredBuffer<int> compute : register(u4);
			//Depth Texture of the currently rendering camera
			sampler2D _CameraDepthTexture;
			//The index of the perspective object, set per-instance via MaterialPropertyBlock's
			int _PerspectiveObjIndex;
			//The index of the currently rendering camera, set in Unity Script
			int _CameraIndexOffset;
			//Whether the current camera should write values to the compute buffer
			//This is especially useful to turn off for the Editor camera
			int _CameraIsActive;

			struct v2f
			{
				float4 pos : SV_POSITION;
				float4 screenPos : TEXCOORD1;
			};

			//Vertex Shader
			v2f vert(appdata_full v)
			{
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.screenPos = ComputeScreenPos(o.pos);
				return o;
			}

			//Fragment Shader
			fixed4 frag(v2f i) : SV_Target
			{
				//Linear Depth of the DepthTexture at this fragment's screen coordinates
				float fts = Linear01Depth(tex2Dproj(_CameraDepthTexture, UNITY_PROJ_COORD(i.screenPos))).r;
				//Linear Depth of this fragment from our vertex input variable
				float ffp = Linear01Depth(i.pos.z).r;
				
				//Important to let this be < 0 (it's a floating point thing methinks)
				float diff = clamp(ffp - fts, -0.1, 1.0);

				if (diff <= 0 && _CameraIsActive == 1)
				{
					//Index is derrived from the Perspective Object Index offset by the currently active Camera Index
					compute[_PerspectiveObjIndex + _CameraIndexOffset] = 1;
				}

				//Clipping can be set to cull the color output. Toggle to help debugging in the Editor
				#if CLIP_ON
					clip(-1);
				#endif
				
				//Otherwise return a colour representation of the fragment depth value
				return i.pos.z;
				}
				ENDCG
			}
		}
		FallBack Off
}