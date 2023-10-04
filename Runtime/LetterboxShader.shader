Shader"Custom/LetterboxShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _SourceRes("Source Resolution", Vector) = (1, 1, 0, 0)
        _TargetRes("Target Resolution", Vector) = (1, 1, 0, 0)
    }
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;            
            float4 _SourceRes;
            float4 _TargetRes;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
    
                float aspectSource = _SourceRes.x / _SourceRes.y;
                float aspectTarget = _TargetRes.x / _TargetRes.y;
    
                if (aspectSource > aspectTarget)
                {
                    
                    float scale = aspectTarget / aspectSource;
                    float offset = (1.0f - scale) * 0.5f;
        
                    if (uv.y < (1.0f - scale) * 0.5f || uv.y > (1.0f + scale) * 0.5f)
                        return half4(0, 0, 0, 1);  // Letterbox
        
                    uv.y = (uv.y - offset) / scale;
                }
                else
                {
                    float scale = aspectSource / aspectTarget;
                    float offset = (1.0f - scale) * 0.5f;
        
                    if (uv.x < (1.0f - scale) * 0.5f || uv.x > (1.0f + scale) * 0.5f)
                        return half4(0, 0, 0, 1); // Pillarbox

                    uv.x = (uv.x - offset) / scale;
                }
                
                return tex2D(_MainTex, uv);
            }
            ENDCG
        }
    }
}