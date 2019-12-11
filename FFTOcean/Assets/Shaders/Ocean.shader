// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'

Shader "Scarecrow/Ocean"
{
    Properties
    {
        _OceanColorShallow ("Ocean Color Shallow", Color) = (1, 1, 1, 1)
        _OceanColorDeep ("Ocean Color Deep", Color) = (1, 1, 1, 1)
        _BubblesColor ("Bubbles Color", Color) = (1, 1, 1, 1)
        _Specular ("Specular", Color) = (1, 1, 1, 1)
        _Gloss ("Gloss", Range(8.0, 256)) = 20
        _FresnelScale ("Fresnel Scale", Range(0, 1)) = 0.5
        _Displace ("Displace", 2D) = "black" { }
        _Normal ("Normal", 2D) = "black" { }
        _Bubbles ("Bubbles", 2D) = "black" { }
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "LightMode" = "ForwardBase" }
        LOD 100
        
        Pass
        {
            CGPROGRAM
            
            #pragma vertex vert
            #pragma fragment frag
            
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            
            struct appdata
            {
                float4 vertex: POSITION;
                float2 uv: TEXCOORD0;
            };
            
            struct v2f
            {
                float4 pos: SV_POSITION;
                float2 uv: TEXCOORD0;
                float3 worldPos: TEXCOORD1;
            };
            
            fixed4 _OceanColorShallow;
            fixed4 _OceanColorDeep;
            fixed4 _BubblesColor;
            fixed4 _Specular;
            float _Gloss;
            fixed _FresnelScale;
            sampler2D _Displace;
            sampler2D _Normal;
            sampler2D _Bubbles;
            float4 _Displace_ST;
            
            inline half3 SamplerReflectProbe(UNITY_ARGS_TEXCUBE(tex), half3 refDir, half roughness, half4 hdr)
            {
                roughness = roughness * (1.7 - 0.7 * roughness);
                half mip = roughness * 6;
                //对反射探头进行采样
                //UNITY_SAMPLE_TEXCUBE_LOD定义在HLSLSupport.cginc，用来区别平台
                half4 rgbm = UNITY_SAMPLE_TEXCUBE_LOD(tex, refDir, mip);
                //采样后的结果包含HDR,所以我们需要将结果转换到RGB
                //定义在UnityCG.cginc
                return DecodeHDR(rgbm, hdr);
            }
            
            
            v2f vert(appdata v)
            {
                v2f o;
                o.uv = TRANSFORM_TEX(v.uv, _Displace);
                float4 displcae = tex2Dlod(_Displace, float4(o.uv, 0, 0));
                v.vertex += float4(displcae.xyz, 0);
                o.pos = UnityObjectToClipPos(v.vertex);
                
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }
            
            fixed4 frag(v2f i): SV_Target
            {
                fixed3 normal = UnityObjectToWorldNormal(tex2D(_Normal, i.uv).rgb);
                fixed bubbles = tex2D(_Bubbles, i.uv).r;
                
                fixed3 lightDir = normalize(UnityWorldSpaceLightDir(i.worldPos));
                fixed3 viewDir = normalize(UnityWorldSpaceViewDir(i.worldPos));
                fixed3 reflectDir = reflect(-viewDir, normal);               
                // reflectDir *= sign(reflectDir.y);
                
                //采样反射探头
                half4 rgbm = UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, reflectDir, 0);
                half3 sky = DecodeHDR(rgbm, unity_SpecCube0_HDR);
                
                //菲涅尔
                fixed fresnel = saturate(_FresnelScale + (1 - _FresnelScale) * pow(1 - dot(normal, viewDir), 5));
                
                half facing = saturate(dot(viewDir, normal));                
                fixed3 oceanColor = lerp(_OceanColorShallow, _OceanColorDeep, facing);
                
                fixed3 ambient = UNITY_LIGHTMODEL_AMBIENT.rgb;
                //泡沫颜色
                fixed3 bubblesDiffuse = _BubblesColor.rbg * _LightColor0.rgb * saturate(dot(lightDir, normal));
                //海洋颜色
                fixed3 oceanDiffuse = oceanColor * _LightColor0.rgb * saturate(dot(lightDir, normal));
                fixed3 halfDir = normalize(lightDir + viewDir);
                fixed3 specular = _LightColor0.rgb * _Specular.rgb * pow(max(0, dot(normal, halfDir)), _Gloss);
                
                fixed3 diffuse = lerp(oceanDiffuse, bubblesDiffuse, bubbles);
                
                fixed3 col = ambient + lerp(diffuse, sky, fresnel) + specular ;
                
                return fixed4(col, 1);
            }
            ENDCG
            
        }
    }
}
