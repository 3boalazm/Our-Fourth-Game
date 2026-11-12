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
            #define DEPTH 0.38
            #define RAD 0.34
            #define TAIL 0.44

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

            float hash21(float2 q) { return frac(sin(dot(q, float2(127.1, 311.7))) * 43758.5453); }

            float sdStar5(float2 q, float r, float rf)
            {
                const float2 k1 = float2(0.809016994, -0.587785252);
                const float2 k2 = float2(-0.809016994, -0.587785252);
                q.x = abs(q.x);
                q -= 2.0 * max(dot(k1, q), 0.0) * k1;
                q -= 2.0 * max(dot(k2, q), 0.0) * k2;
                q.x = abs(q.x);
                q.y -= r;
                float2 ba = rf * float2(-k1.y, k1.x) - float2(0, 1);
                float h = clamp(dot(q, ba) / dot(ba, ba), 0.0, r);
                return length(q - ba * h) * sign(q.y * ba.x - q.x * ba.y);
            }

            float sdTri(float2 q, float r)
            {
                const float k = 1.7320508;
                q.x = abs(q.x) - r;
                q.y = q.y + r / k;
                if (q.x + k * q.y > 0.0) q = float2(q.x - k * q.y, -k * q.x - q.y) / 2.0;
                q.x -= clamp(q.x, -2.0 * r, 0.0);
                return -length(q) * sign(q.y);
            }

            float sdBox(float2 q, float2 b)
            {
                float2 d = abs(q) - b;
                return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
            }

            // Decorations on the floor: one shape in each 7x7 block, placed, turned,
            // sized and coloured by a hash so it is the same on every device.
            fixed3 Deco(float2 p, fixed3 col)
            {
                const float B = 7.0;
                float2 blk = floor(p / B);
                float h1 = hash21(blk), h2 = hash21(blk + 17.3), h3 = hash21(blk + 41.9), h4 = hash21(blk + 73.1);
                if (h4 < 0.18) return col; // some blocks stay empty
                float2 q = p - (blk + 0.5) * B - (float2(h1, h2) - 0.5) * 2.4;
                float a = h3 * 6.2831 + _Time.y * 0.15 * (h1 - 0.5);
                float cs = cos(a), sn = sin(a);
                q = float2(cs * q.x - sn * q.y, sn * q.x + cs * q.y);
                float size = lerp(0.9, 1.5, h2);
                float type = floor(h1 * 5.0);
                float d;
                if (type < 0.5) d = sdStar5(q, size, 0.5) - 0.12;
                else if (type < 1.5) d = abs(length(q) - size * 0.8) - 0.16;
                else if (type < 2.5) d = sdTri(q, size * 0.75) - 0.12;
                else if (type < 3.5) d = min(sdBox(q, float2(size, 0.22)), sdBox(q, float2(0.22, size))) - 0.06;
                else d = min(length(q - float2(-0.6, 0)) - 0.32, min(length(q - float2(0.6, 0)) - 0.32, length(q - float2(0, 0.9)) - 0.32));
                // some are just an outline
                if (h3 > 0.55 && type < 2.5) d = abs(d + 0.14) - 0.12;
                float k = 1.0 - smoothstep(-0.04, 0.04, d);
                fixed3 tint = palette(1.0 + floor(h4 * 3.999));
                fixed3 deco = lerp(col, tint, 0.16);
                deco = lerp(deco, fixed3(1, 1, 1), 0.25 * step(0.6, h2));
                return lerp(col, deco, k);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 p = i.uv * _Grid.xy;
                float2 c = floor(p);
                float2 f = p - c;

                // ---- ground: one smooth colour with soft pastel shapes scattered on it
                fixed3 col = _Ground.rgb;
                float wall = min(min(p.x, _Grid.x - p.x), min(p.y, _Grid.y - p.y));
                col = lerp(_Ground2.rgb, col, smoothstep(0.0, 6.0, wall));
                col = Deco(p, col);

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
                        // the side gets darker towards the floor (y: 0 at the floor, 1 under the top)
                        col = land * (near > 0.5 ? 0.7 : 0.56);
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
                    float ms = skel(d + float2(-0.06, 0.16), al, ar, ad, au);
                    float aa = 0.035;
                    // soft shadow under the tube
                    col *= 1.0 - 0.2 * (1 - smoothstep(TAIL - 0.1, TAIL + 0.08, ms));
                    float a = 1 - smoothstep(TAIL - aa, TAIL, m);
                    // a glossy jelly tube: lighter than the land, rounder shading across it,
                    // a bright streak to the top left and a slow shimmer along it
                    fixed3 base = lerp(palette(t), fixed3(1, 1, 1), 0.2);
                    float k = saturate(m / TAIL);
                    fixed3 tc = base * lerp(1.06, 0.8, k * k);
                    float hi = skel(d + float2(0.1, -0.12), al, ar, ad, au);
                    tc = lerp(tc, fixed3(1, 1, 1), (1 - smoothstep(0.02, 0.11, hi)) * 0.55 * step(m, TAIL - 0.08));
                    float wave = 0.5 + 0.5 * sin((p.x + p.y) * 1.6 - _Time.y * 6.0);
                    tc = lerp(tc, saturate(tc + 0.1), wave * 0.5);
                    tc = lerp(tc, palette(t) * 0.7, smoothstep(TAIL - 0.07, TAIL - 0.01, m));
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
