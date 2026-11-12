// Draws the whole arena from a tiny "cell map" texture (one pixel per cell):
//   R = owner (0..4) / 5, G = trail owner (0..4) / 5, B = capture flash (1 = just taken)
// Land is drawn as soft rounded blobs with a darker rim and a light top edge,
// trails as glowing animated stripes, empty ground as a two-tone checkerboard.
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
        _Ground ("Ground", Color) = (0.93, 0.94, 0.98, 1)
        _Ground2 ("Ground 2", Color) = (0.88, 0.90, 0.96, 1)
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
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _Grid;
            fixed4 _C1, _C2, _C3, _C4, _Ground, _Ground2;

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

            fixed3 palette(float k)
            {
                if (k < 1.5) return _C1.rgb;
                if (k < 2.5) return _C2.rgb;
                if (k < 3.5) return _C3.rgb;
                return _C4.rgb;
            }

            // 1 where this cell's blob is solid, rounding the outside corners.
            float blob(float2 f, float l, float r, float d, float u)
            {
                float rad = 0.42;
                float2 q = f;
                float inside = 1.0;
                // corners where both neighbours are different: cut a quarter circle
                if (l < 0.5 && d < 0.5 && q.x < rad && q.y < rad) inside = step(length(q - rad), rad);
                if (r < 0.5 && d < 0.5 && q.x > 1 - rad && q.y < rad) inside = step(length(q - float2(1 - rad, rad)), rad);
                if (l < 0.5 && u < 0.5 && q.x < rad && q.y > 1 - rad) inside = step(length(q - float2(rad, 1 - rad)), rad);
                if (r < 0.5 && u < 0.5 && q.x > 1 - rad && q.y > 1 - rad) inside = step(length(q - float2(1 - rad, 1 - rad)), rad);
                return inside;
            }

            // Distance (in cells) to the nearest edge that faces a different owner.
            float edgeDist(float2 f, float l, float r, float d, float u)
            {
                float e = 1.0;
                if (l < 0.5) e = min(e, f.x);
                if (r < 0.5) e = min(e, 1 - f.x);
                if (d < 0.5) e = min(e, f.y);
                if (u < 0.5) e = min(e, 1 - f.y);
                return e;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 p = i.uv * _Grid.xy;
                float2 c = floor(p);
                float2 f = p - c;

                float4 s = cellAt(c);
                float o = ownerOf(s);
                float t = trailOf(s);

                // ground: soft checkerboard with a faint grid line
                float chk = fmod(c.x + c.y, 2.0);
                fixed3 col = lerp(_Ground.rgb, _Ground2.rgb, chk);
                float g = min(min(f.x, 1 - f.x), min(f.y, 1 - f.y));
                col *= 0.985 + 0.015 * smoothstep(0.0, 0.06, g);
                // land is "raised": it casts a short shadow onto the ground just below it
                if (o < 0.5 && ownerOf(cellAt(c + float2(0, 1))) > 0.5)
                    col *= 1.0 - 0.16 * smoothstep(0.35, 1.0, f.y);

                if (o > 0.5)
                {
                    float l = ownerOf(cellAt(c + float2(-1, 0))) == o ? 1 : 0;
                    float r = ownerOf(cellAt(c + float2(1, 0))) == o ? 1 : 0;
                    float d = ownerOf(cellAt(c + float2(0, -1))) == o ? 1 : 0;
                    float u = ownerOf(cellAt(c + float2(0, 1))) == o ? 1 : 0;
                    float solid = blob(f, l, r, d, u);
                    if (solid > 0.5)
                    {
                        fixed3 land = palette(o);
                        float e = edgeDist(f, l, r, d, u);
                        // darker rim on the outline, light bevel on top edges, faint diagonal pattern inside
                        float rim = 1 - smoothstep(0.0, 0.16, e);
                        fixed3 shade = lerp(land, land * 0.66, rim);
                        // thicker, darker bottom edge: the side of the raised tile
                        if (d < 0.5) shade = lerp(shade, land * 0.55, 1 - smoothstep(0.08, 0.24, f.y));
                        if (u < 0.5) shade = lerp(shade, saturate(land * 1.25 + 0.08), (1 - smoothstep(0.06, 0.2, 1 - f.y)) * 0.55);
                        float stripes = step(0.5, frac((p.x + p.y) * 0.25));
                        shade *= 0.97 + 0.03 * stripes;
                        col = shade;
                    }
                    else
                    {
                        // soft shadow just outside rounded corners
                        col *= 0.93;
                    }
                }

                if (t > 0.5)
                {
                    fixed3 base = palette(t);
                    fixed3 glow = lerp(base, fixed3(1, 1, 1), 0.45);
                    float stripe = 0.5 + 0.5 * sin((p.x + p.y) * 3.1416 - _Time.y * 7.0);
                    float edge = min(min(f.x, 1 - f.x), min(f.y, 1 - f.y));
                    fixed3 trailCol = lerp(base, glow, 0.35 + 0.35 * stripe);
                    trailCol = lerp(trailCol * 0.8, trailCol, smoothstep(0.0, 0.2, edge));
                    col = lerp(col, trailCol, 0.88);
                }

                // capture flash: a bright pulse that fades
                float fl = s.b;
                col = lerp(col, fixed3(1, 1, 1), fl * fl * 0.75);

                return fixed4(col * i.color.rgb, 1);
            }
            ENDCG
        }
    }
}
