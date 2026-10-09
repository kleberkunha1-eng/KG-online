Shader "TOP/WorldSkyBlend"
{
    Properties
    {
        _Sky0 ("Day", 2D) = "white" {}
        _Sky1 ("Sunless", 2D) = "white" {}
        _Sky2 ("Rainy", 2D) = "white" {}
        _Sky3 ("Snowy", 2D) = "white" {}
        _Sky4 ("Sunrise", 2D) = "white" {}
        _Sky5 ("Sunset", 2D) = "white" {}
        _Sky6 ("Night", 2D) = "white" {}
        _Sky7 ("Moonless", 2D) = "white" {}
        _Exposure ("Exposure", Float) = 1
        _Rotation ("Rotation", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _Sky0, _Sky1, _Sky2, _Sky3, _Sky4, _Sky5, _Sky6, _Sky7;
            float4 _WeightsA, _WeightsB;
            float _Exposure, _Rotation, _SunRadius, _SunBrightness;
            float3 _SunDirection;
            float3 _MoonDirection;
            float _MoonRadius, _MoonBrightness;
            struct v2f { float4 position : SV_POSITION; float3 direction : TEXCOORD0; };
            float lunarCrater(float2 uv, float2 center, float radius)
            {
                float distance = length(uv-center)/radius;
                float bowl = 1 - smoothstep(.5,.95,distance);
                float rim = exp(-pow((distance-.95)*14,2));
                return 1 - .22*bowl + .12*rim;
            }
            v2f vert(float4 vertex : POSITION)
            {
                v2f o;
                o.position = UnityObjectToClipPos(vertex);
                o.direction = vertex.xyz;
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float3 direction = normalize(i.direction);
                float angle = radians(_Rotation);
                float3 rotated = float3(cos(angle)*direction.x - sin(angle)*direction.z,
                    direction.y, sin(angle)*direction.x + cos(angle)*direction.z);
                float2 uv = float2(.5 - atan2(rotated.z, rotated.x)/(2*UNITY_PI),
                    1 - acos(clamp(rotated.y,-1,1))/UNITY_PI);
                float3 color = 0;
                if (_WeightsA.x > .00001) color += tex2D(_Sky0,uv).rgb * _WeightsA.x;
                if (_WeightsA.y > .00001) color += tex2D(_Sky1,uv).rgb * _WeightsA.y;
                if (_WeightsA.z > .00001) color += tex2D(_Sky2,uv).rgb * _WeightsA.z;
                if (_WeightsA.w > .00001) color += tex2D(_Sky3,uv).rgb * _WeightsA.w;
                if (_WeightsB.x > .00001) color += tex2D(_Sky4,uv).rgb * _WeightsB.x;
                if (_WeightsB.y > .00001) color += tex2D(_Sky5,uv).rgb * _WeightsB.y;
                if (_WeightsB.z > .00001) color += tex2D(_Sky6,uv).rgb * _WeightsB.z;
                if (_WeightsB.w > .00001) color += tex2D(_Sky7,uv).rgb * _WeightsB.w;
                float distance = acos(clamp(dot(direction,normalize(_SunDirection)),-1,1));
                float disc = 1 - smoothstep(_SunRadius*.8,_SunRadius,distance);
                float halo = exp(-distance*distance/max(_SunRadius*_SunRadius*12,.000001))*.12;
                float horizon = smoothstep(-.01,.01,direction.y);
                color = color*_Exposure + float3(1,.83,.55)*(disc+halo)*_SunBrightness*horizon;
                if (_MoonBrightness > .00001)
                {
                    float3 moonDirection = normalize(_MoonDirection);
                    float moonDistance = acos(clamp(dot(direction, moonDirection), -1, 1));
                    float moonDisc = 1 - smoothstep(_MoonRadius*.94, _MoonRadius, moonDistance);
                    float3 reference = abs(moonDirection.y) > .99 ? float3(0,0,1) : float3(0,1,0);
                    float3 right = normalize(cross(reference, moonDirection));
                    float3 up = cross(moonDirection, right);
                    float2 lunarUV = float2(dot(direction,right), dot(direction,up)) / max(_MoonRadius,.000001);
                    float relief = .86 + .035*sin(lunarUV.x*39 + sin(lunarUV.y*27))
                        + .025*sin(lunarUV.y*57 + lunarUV.x*33);
                    float mare = 1 - .32*exp(-dot(lunarUV-float2(-.28,.3),lunarUV-float2(-.28,.3))*10)
                        - .22*exp(-dot(lunarUV-float2(.24,.15),lunarUV-float2(.24,.15))*22)
                        - .18*exp(-dot(lunarUV-float2(-.12,-.2),lunarUV-float2(-.12,-.2))*30);
                    float craters = lunarCrater(lunarUV,float2(.35,-.4),.17)
                        * lunarCrater(lunarUV,float2(-.48,-.25),.12)
                        * lunarCrater(lunarUV,float2(.1,.65),.1)
                        * lunarCrater(lunarUV,float2(.62,.15),.08)
                        * lunarCrater(lunarUV,float2(-.15,-.65),.13);
                    float limb = sqrt(saturate(1-dot(lunarUV,lunarUV)));
                    float3 lunarColor = float3(.78,.81,.86)*relief*mare*craters*(.7+.3*limb)*_MoonBrightness;
                    color = lerp(color, lunarColor, moonDisc*horizon);
                }
                return float4(color,1);
            }
            ENDCG
        }
    }
    Fallback Off
}
