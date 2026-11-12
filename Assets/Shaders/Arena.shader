// Draws the whole arena from a tiny "cell map" texture (one pixel per cell):
//   R = owner (0..4) / 5, G = trail owner (0..4) / 5, B = capture flash (1 = just taken)
// Land is drawn as raised tiles: a rounded top face, a darker side face below it and a
// soft shadow on the ground. Tails are smooth rounded bands that follow the path, with
// a moving shine. Empty ground is a soft two-tone checkerboard that darkens at the walls.
Shader "OurFourth/Arena"
{
    Properties
    {
        _MainTex ("Cells", 2D) = "black" {}
        _Grid ("Grid size (cells)", Vector) = (48, 48, 0, 0)
        _C1 ("Player 1", Color) = (0.30, 0.82, 0.45, 1)
        _C2 ("Player 2", Color) = (1.00, 0.42, 0.66, 1)
        _C3 ("Player 3", Color) = (0.30, 0.62, 1.00, 1)
        _C4 ("Player 4", Color) = (1.00, 0.66, 0.22, 1)
        _Ground ("Ground", Color) = (0.94, 0.95, 0.99, 1)
        _Ground2 ("Ground 2", Color) = (0.89, 0.91, 0.97, 1)
    }
    SubShader
    {
        // Transparent queue so it sorts with the sprites (shadow below, players above) by sorting order.
        Tags { "Queue" = "Transparent" "RenderType" = "Opaque" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _Grid;
            fixed4 _C1, _C2, _C3, _C4, _Ground, _Ground2;

            // how tall the land looks (in cells) and the tail's half width
            #define DEPTH 0.30
            #define RAD 0.34
            #define TAIL 0.34

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            float4 cellAt(float2 c)
            {
                // outside the arena counts as "nobody"
                if (c.x < 0 || c.y < 0 || c.x >= _Grid.x || c.y >= _Grid.y) return float4(0, 0, 0, 1);
                return tex2D(_MainTex, (c + 0.5) / _Grid.xy);
            }

            float ownerOf(float4 s) { return floor(s.r * 5.0 + 0.5); }
            float trailOf(float4 s) { return floor(s.g * 5.0 + 0.5); }
            float ownerAt(float2 c) { return ownerOf(cellAt(c)); }
            float trailAt(float2 c) { return trailOf(cellAt(c)); }

            fixed3 palette(float k)
            {
                if (k < 1.5) return _C1.rgb;
                if (k < 2.5) return _C2.rgb;
                if (k < 3.5) return _C3.rgb;
                return _C4.rgb;
            }

            // Owner whose raised top face covers point q (in cells), 0 when none.
            // e = distance to the nearest edge that faces a different owner.
            float topAt(float2 q, out float e)
            {
                float2 c = floor(q);
                float2 f = q - c;
                e = 1.0;
                float o = ownerAt(c);
                if (o < 0.5) return 0;
                float l = ownerAt(c + float2(-1, 0)) == o ? 1 : 0;
                float r = ownerAt(c + float2(1, 0)) == o ? 1 : 0;
                float d = ownerAt(c + float2(0, -1)) == o ? 1 : 0;
                float u = ownerAt(c + float2(0, 1)) == o ? 1 : 0;
                // round the outside corners (both neighbours different)
                if (l < 0.5 && d < 0.5 && f.x < RAD && f.y < RAD && length(f - RAD) > RAD) return 0;
                if (r < 0.5 && d < 0.5 && f.x > 1 - RAD && f.y < RAD && length(f - float2(1 - RAD, RAD)) > RAD) return 0;
                if (l < 0.5 && u < 0.5 && f.x < RAD && f.y > 1 - RAD && length(f - float2(RAD, 1 - RAD)) > RAD) return 0;
                if (r < 0.5 && u < 0.5 && f.x > 1 - RAD && f.y > 1 - RAD && length(f - float2(1 - RAD, 1 - RAD)) > RAD) return 0;
                if (l < 0.5) e = min(e, f.x);
                if (r < 0.5) e = min(e, 1 - f.x);
                if (d < 0.5) e = min(e, f.y);
                if (u < 0.5) e = min(e, 1 - f.y);
                // distance to a rounded corner's arc
                if (l < 0.5 && d < 0.5 && f.x < RAD && f.y < RAD) e = min(e, RAD - length(f - RAD));
                if (r < 0.5 && d < 0.5 && f.x > 1 - RAD && f.y < RAD) e = min(e, RAD - length(f - float2(1 - RAD, RAD)));
                if (l < 0.5 && u < 0.5 && f.x < RAD && f.y > 1 - RAD) e = min(e, RAD - length(f - float2(RAD, 1 - RAD)));
                if (r < 0.5 && u < 0.5 && f.x > 1 - RAD && f.y > 1 - RAD) e = min(e, RAD - length(f - float2(1 - RAD, 1 - RAD)));
                return o;
            }

            // Distance from d (relative to the cell centre) to a band that joins the centre
            // with the connected sides of the cell.
            float skel(float2 d, float l, float r, float dn, float u)
            {
                float m = length(d);
                if (r > 0.5 && d.x > 0) m = min(m, abs(d.y));
                if (l > 0.5 && d.x < 0) m = min(m, abs(d.y));
                if (u > 0.5 && d.y > 0) m = min(m, abs(d.x));
                if (dn > 0.5 && d.y < 0) m = min(m, abs(d.x));
                return m;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 p = i.uv * _Grid.xy;
                float2 c = floor(p);
                float2 f = p - c;

                // ---- ground: soft checkerboard, faint grid, darker near the walls
                float chk = fmod(c.x + c.y, 2.0);
                fixed3 col = lerp(_Ground.rgb, _Ground2.rgb, chk);
                float g = min(min(f.x, 1 - f.x), min(f.y, 1 - f.y));
                col *= 0.985 + 0.015 * smoothstep(0.0, 0.05, g);
                float wall = min(min(p.x, _Grid.x - p.x), min(p.y, _Grid.y - p.y));
                col *= lerp(0.86, 1.0, smoothstep(0.0, 5.0, wall));

                // ---- land
                float e;
                float top = topAt(p, e);
                if (top > 0.5)
                {
                    fixed3 land = palette(top);
                    // light top edge, darker rim all round, a faint diagonal sheen
                    float rim = 1 - smoothstep(0.0, 0.10, e);
                    fixed3 shade = land * (1.0 + 0.022 * step(0.5, frac((p.x + p.y) * 0.25)));
                    shade = lerp(shade, land * 0.78, rim * 0.8);
                    // a highlight line just inside the outline
                    float hl = smoothstep(0.08, 0.13, e) * (1 - smoothstep(0.13, 0.2, e));
                    shade = lerp(shade, saturate(land * 1.2 + 0.12), hl * 0.5);
                    col = shade;
                }
                else
                {
                    // the side face of a tile above this point, then its shadow on the ground
                    float e2;
                    float up = topAt(p + float2(0, DEPTH), e2);
                    if (up > 0.5)
                    {
                        fixed3 land = palette(up);
                        float e3;
                        float near = topAt(p + float2(0, DEPTH * 0.45), e3);
                        col = land * (near > 0.5 ? 0.66 : 0.56);
                    }
                    else
                    {
                        float e4, e5;
                        float s1 = topAt(p + float2(-0.08, DEPTH + 0.12), e4);
                        float s2 = topAt(p + float2(-0.16, DEPTH + 0.28), e5);
                        col *= 1.0 - 0.13 * (s1 > 0.5 ? 1 : 0) - 0.07 * (s2 > 0.5 ? 1 : 0);
                    }
                }

                // ---- tail: a rounded band that follows the path
                float4 s = cellAt(c);
                float t = trailOf(s);
                if (t > 0.5)
                {
                    float al = trailAt(c + float2(-1, 0)) == t ? 1 : 0;
                    float ar = trailAt(c + float2(1, 0)) == t ? 1 : 0;
                    float ad = trailAt(c + float2(0, -1)) == t ? 1 : 0;
                    float au = trailAt(c + float2(0, 1)) == t ? 1 : 0;
                    if (al + ar + ad + au < 1.5)
                    {
                        // an end of the tail: join it to the owner's land next to it
                        if (ownerAt(c + float2(-1, 0)) == t) al = 1;
                        else if (ownerAt(c + float2(1, 0)) == t) ar = 1;
                        else if (ownerAt(c + float2(0, -1)) == t) ad = 1;
                        else if (ownerAt(c + float2(0, 1)) == t) au = 1;
                    }
                    float2 d = f - 0.5;
                    float m = skel(d, al, ar, ad, au);
                    float ms = skel(d + float2(-0.05, 0.10), al, ar, ad, au);
                    float aa = 0.04;
                    col *= 1.0 - 0.22 * (1 - smoothstep(TAIL - aa, TAIL + 0.05, ms));
                    float a = 1 - smoothstep(TAIL - aa, TAIL, m);
                    fixed3 base = palette(t);
                    float wave = 0.5 + 0.5 * sin((p.x + p.y) * 2.2 - _Time.y * 9.0);
                    fixed3 tc = lerp(base, saturate(base * 1.15 + 0.22), 0.25 + 0.45 * wave);
                    tc = lerp(tc, base * 0.72, smoothstep(TAIL - 0.12, TAIL - 0.03, m));
                    col = lerp(col, tc, a);
                }

                // ---- capture flash: a bright pulse that fades
                float fl = s.b;
                col = lerp(col, fixed3(1, 1, 1), fl * fl * 0.6);

                return fixed4(col * i.color.rgb, 1);
            }
            ENDCG
        }
    }
}
