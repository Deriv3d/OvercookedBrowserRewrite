// Replacement for the game's "Hidden/FXAA III (Console)" (AssetRipper Free exports every shader as a dummy).
// Unity Standard Assets 5.x port of NVIDIA FXAA 3.11 (console variant), used by the Antialiasing
// script in FXAA3Console mode; rewritten for Unity 2022.3 / WebGL 2. Uniforms _EdgeThresholdMin,
// _EdgeThreshold, _EdgeSharpness are what the script sets.
Shader "Hidden/FXAA III (Console)" {
	Properties {
		_MainTex ("-", 2D) = "white" {}
		_EdgeThresholdMin ("Edge threshold min", float) = 0.125
		_EdgeThreshold ("Edge Threshold", float) = 0.25
		_EdgeSharpness ("Edge sharpness", float) = 4.0
	}

	SubShader {
		Pass {
			ZTest Always Cull Off ZWrite Off

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0
			#include "UnityCG.cginc"

			uniform sampler2D _MainTex;
			uniform half _EdgeThresholdMin;
			uniform half _EdgeThreshold;
			uniform half _EdgeSharpness;
			float4 _MainTex_TexelSize;

			struct v2f {
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
				float4 interpolatorA : TEXCOORD1;
				float4 interpolatorB : TEXCOORD2;
				float4 interpolatorC : TEXCOORD3;
			};

			v2f vert (appdata_img v)
			{
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.uv = v.texcoord.xy;

				float2 offset = _MainTex_TexelSize.xy * 0.5;
				float4 extents;
				extents.xy = v.texcoord.xy - offset;
				extents.zw = v.texcoord.xy + offset;

				float4 rcpSize;
				rcpSize.xy = -_MainTex_TexelSize.xy * 0.5;
				rcpSize.zw = _MainTex_TexelSize.xy * 0.5;

				o.interpolatorA = extents;
				o.interpolatorB = rcpSize;
				o.interpolatorC = rcpSize * 4.0;
				return o;
			}

			#define FxaaTexTop(t, p) tex2Dlod(t, float4(p, 0.0, 0.0))

			inline float TexLuminance (float4 c)
			{
				return Luminance(c.rgb);
			}

			float4 FxaaPixelShader (float2 pos, float4 posPos, sampler2D tex, float4 rcpFrameOpt, float4 rcpFrameOpt2,
				float edgeSharpness, float edgeThreshold, float edgeThresholdMin)
			{
				float lumaNw = TexLuminance(FxaaTexTop(tex, posPos.xy));
				float lumaSw = TexLuminance(FxaaTexTop(tex, posPos.xw));
				float lumaNe = TexLuminance(FxaaTexTop(tex, posPos.zy));
				float lumaSe = TexLuminance(FxaaTexTop(tex, posPos.zw));

				float4 rgbyM = FxaaTexTop(tex, pos.xy);
				float lumaM = TexLuminance(rgbyM);

				float lumaMaxNwSw = max(lumaNw, lumaSw);
				lumaNe += 1.0 / 384.0;
				float lumaMinNwSw = min(lumaNw, lumaSw);

				float lumaMaxNeSe = max(lumaNe, lumaSe);
				float lumaMinNeSe = min(lumaNe, lumaSe);

				float lumaMax = max(lumaMaxNeSe, lumaMaxNwSw);
				float lumaMin = min(lumaMinNeSe, lumaMinNwSw);

				float lumaMaxScaled = lumaMax * edgeThreshold;

				float lumaMinM = min(lumaMin, lumaM);
				float lumaMaxScaledClamped = max(edgeThresholdMin, lumaMaxScaled);
				float lumaMaxM = max(lumaMax, lumaM);
				float dirSwMinusNe = lumaSw - lumaNe;
				float lumaMaxSubMinM = lumaMaxM - lumaMinM;
				float dirSeMinusNw = lumaSe - lumaNw;
				if (lumaMaxSubMinM < lumaMaxScaledClamped)
					return rgbyM;

				float2 dir;
				dir.x = dirSwMinusNe + dirSeMinusNw;
				dir.y = dirSwMinusNe - dirSeMinusNw;

				float2 dir1 = normalize(dir.xy);
				float4 rgbyN1 = FxaaTexTop(tex, pos.xy - dir1 * rcpFrameOpt.zw);
				float4 rgbyP1 = FxaaTexTop(tex, pos.xy + dir1 * rcpFrameOpt.zw);

				float dirAbsMinTimesC = min(abs(dir1.x), abs(dir1.y)) * edgeSharpness;
				float2 dir2 = clamp(dir1.xy / dirAbsMinTimesC, -2.0, 2.0);

				float4 rgbyN2 = FxaaTexTop(tex, pos.xy - dir2 * rcpFrameOpt2.zw);
				float4 rgbyP2 = FxaaTexTop(tex, pos.xy + dir2 * rcpFrameOpt2.zw);

				float4 rgbyA = rgbyN1 + rgbyP1;
				float4 rgbyB = ((rgbyN2 + rgbyP2) * 0.25) + (rgbyA * 0.25);

				float lumaB = TexLuminance(rgbyB);
				bool twoTap = (lumaB < lumaMin) || (lumaB > lumaMax);
				if (twoTap)
					rgbyB.xyz = rgbyA.xyz * 0.5;
				return rgbyB;
			}

			half4 frag (v2f i) : SV_Target
			{
				return FxaaPixelShader(i.uv.xy, i.interpolatorA, _MainTex, i.interpolatorB, i.interpolatorC,
					_EdgeSharpness, _EdgeThreshold, _EdgeThresholdMin);
			}
			ENDCG
		}
	}

	Fallback Off
}
