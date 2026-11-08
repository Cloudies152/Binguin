using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Binguin.Feature.Band
{
    /// <summary>
    /// 演唱会「现场视角」（2026-11-08）。
    ///
    /// 从观众席正面看舞台：舞台、乐器、乐手、灯光、观众都按**场地上的实际情况**显示：
    ///   - 乐器：只画舞台上真实存在的那几件（找不到就空着）
    ///   - 乐手：只画正在演出的那几个人（没有就空位）
    ///   - 灯光：场地 25 格内数到几盏灯就画几道光柱（0 盏就没有光柱）
    ///   - 观众：有观众才画前景剪影
    /// </summary>
    public class Window_BinguinConcertScene : Window
    {
        private static readonly string[] SlotDefNames =
        {
            "Binguin_Band_Microphone",
            "Binguin_Band_Guitar",
            "Binguin_Band_Keyboard",
            "Binguin_Band_DrumKit",
            "Binguin_Band_Bass"
        };

        private static readonly string[] SlotTexPaths =
        {
            "Things/Building/Joy/Binguin/UI/Band_Microphone",
            "Things/Building/Joy/Binguin/UI/Band_Guitar",
            "Things/Building/Joy/Binguin/UI/Band_Keyboard",
            "Things/Building/Joy/Binguin/UI/Band_DrumKit",
            "Things/Building/Joy/Binguin/UI/Band_Bass"
        };

        private static readonly Color[] BeamColors =
        {
            new Color(0.92f, 0.42f, 0.55f),
            new Color(0.45f, 0.72f, 1.00f),
            new Color(0.98f, 0.86f, 0.40f),
            new Color(0.55f, 0.95f, 0.70f)
        };

        private readonly Thing stage;
        private readonly Map map;
        private readonly IntVec3 center;

        private readonly List<Pawn> performers = new List<Pawn>();
        private readonly Thing[] instruments = new Thing[5];
        private int lights;
        private int audience;
        private int lastRefresh = -999999;

        public Window_BinguinConcertScene(Thing stage)
        {
            this.stage = stage;
            map = stage != null ? stage.Map : null;
            center = stage != null ? stage.Position : IntVec3.Invalid;
            doCloseButton = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            forcePause = false;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(880f, 600f); }
        }

        private static Texture2D Tex(string path)
        {
            return ContentFinder<Texture2D>.Get(path, false);
        }

        private void Refresh()
        {
            performers.Clear();
            audience = 0;
            lights = 0;
            for (int i = 0; i < instruments.Length; i++)
            {
                instruments[i] = null;
            }
            if (map == null)
            {
                return;
            }

            GameComponent_BinguinPerformanceState st = GameComponent_BinguinPerformanceState.Current;
            if (st != null)
            {
                List<Pawn> src = st.selfHosted ? st.performers : st.visitingPerformers;
                if (src != null)
                {
                    for (int i = 0; i < src.Count; i++)
                    {
                        Pawn p = src[i];
                        if (p == null || p.Dead || !p.Spawned || p.Map != map || !p.RaceProps.Humanlike)
                        {
                            continue;
                        }
                        performers.Add(p);
                    }
                }
                List<Pawn> aud = st.selfHosted ? st.audience : st.visitingAudience;
                if (aud != null)
                {
                    audience = aud.Count;
                }
            }

            lights = BinguinConcertQuality.CountLights(map, center);
            for (int i = 0; i < SlotDefNames.Length; i++)
            {
                instruments[i] = FindNearest(SlotDefNames[i]);
            }
        }

        private Thing FindNearest(string defName)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null || map == null)
            {
                return null;
            }
            Thing best = null;
            int bestD = int.MaxValue;
            List<Thing> list = map.listerThings.ThingsOfDef(def);
            for (int i = 0; i < list.Count; i++)
            {
                Thing t = list[i];
                if (t == null || t.Destroyed || !t.Spawned)
                {
                    continue;
                }
                int d = (t.Position - center).LengthHorizontalSquared;
                if (d < bestD)
                {
                    bestD = d;
                    best = t;
                }
            }
            return best;
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            int now = Find.TickManager.TicksGame;
            if (now - lastRefresh > 120)
            {
                lastRefresh = now;
                Refresh();
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (lastRefresh < 0)
            {
                Refresh();
            }

            // ---- 场地背景 ----
            Texture2D bg = Tex("UI/Binguin/Concert/VenueBg");
            if (bg != null)
            {
                GUI.DrawTexture(inRect, bg, ScaleMode.ScaleAndCrop);
            }
            else
            {
                Widgets.DrawBoxSolid(inRect, new Color(0.03f, 0.03f, 0.05f));
            }

            // ---- 灯光：场地有灯才画光柱 ----
            int beamCount = Mathf.Min(lights, 4);
            if (beamCount > 0)
            {
                Texture2D beam = Tex("UI/Binguin/Concert/SpotBeam");
                if (beam != null)
                {
                    float t = Time.realtimeSinceStartup;
                    for (int i = 0; i < beamCount; i++)
                    {
                        float f = beamCount == 1 ? 0.5f : i / (float)(beamCount - 1);
                        float x = Mathf.Lerp(150f, 730f, f);
                        Color c = BeamColors[i % BeamColors.Length];
                        c.a = 0.50f + 0.16f * Mathf.Sin(t * 2.1f + i * 1.7f);
                        GUI.color = c;
                        GUI.DrawTexture(new Rect(x - 110f, -40f, 220f, 420f), beam);
                    }
                    GUI.color = Color.white;
                }
            }

            // ---- 舞台 ----
            Texture2D stageTex = Tex("UI/Binguin/Concert/StageSlab");
            if (stageTex != null)
            {
                GUI.DrawTexture(new Rect(0f, 330f, inRect.width, 250f), stageTex);
            }

            // ---- 5 个位置：乐手 + 乐器（按游戏内真实布局，先画后排再画前排） ----
            for (int row = 0; row < 2; row++)
            {
                bool backRow = row == 1;
                for (int i = 0; i < 5; i++)
                {
                    if (SlotBackRow[i] != backRow)
                    {
                        continue;
                    }
                    float cx = inRect.width * SlotXFactor[i];
                    float scale = backRow ? 0.76f : 1f;
                    float size = 150f * scale;
                    float portraitY = backRow ? 190f : 240f;
                    float instY = backRow ? 292f : 358f;

                    Pawn pawn = i < performers.Count ? performers[i] : null;
                    if (pawn != null)
                    {
                        RenderTexture rt = PortraitsCache.Get(pawn, new Vector2(size, size), Rot4.South,
                            default(Vector3), 1f, true, true, true, true, null, null, false, null);
                        if (rt != null)
                        {
                            GUI.DrawTexture(new Rect(cx - size / 2f, portraitY, size, size), rt);
                        }
                    }
                    else
                    {
                        Widgets.DrawBoxSolid(new Rect(cx - size * 0.28f, portraitY + size * 0.42f, size * 0.56f, size * 0.58f),
                            new Color(0f, 0f, 0f, 0.22f));
                    }

                    Thing inst = instruments[i];
                    if (inst != null)
                    {
                        Texture2D it = Tex(SlotTexPaths[i]);
                        if (it != null)
                        {
                            float w = size;
                            float h = Mathf.Min(size, w * it.height / Mathf.Max(1, it.width));
                            Widgets.DrawBoxSolid(new Rect(cx - w / 2f, instY + h * 0.5f, w, h * 0.4f), new Color(0f, 0f, 0f, 0.18f));
                            GUI.DrawTexture(new Rect(cx - w / 2f, instY, w, h), it);
                        }
                    }
                }
            }

            // ---- 观众剪影（有观众才画） ----
            if (audience > 0)
            {
                Texture2D crowd = Tex("UI/Binguin/Concert/CrowdRow");
                if (crowd != null)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.92f);
                    GUI.DrawTexture(new Rect(0f, 505f, inRect.width, 110f), crowd);
                    GUI.color = Color.white;
                }
            }

            // ---- 顶部信息 ----
            Widgets.Label(new Rect(16f, 8f, 420f, 30f),
                "Binguin_ConcertScene_Title".Translate().ToString());
            string info = "Binguin_ConcertScene_Lights".Translate(lights.ToString())
                + "   " + "Binguin_ConcertScene_Audience".Translate(audience.ToString());
            Widgets.Label(new Rect(16f, 34f, 420f, 26f), info);

            if (performers.Count == 0)
            {
                Widgets.Label(new Rect(inRect.width / 2f - 160f, 260f, 320f, 30f),
                    "Binguin_ConcertScene_NoBand".Translate().ToString());
            }
        }

        /// <summary>
        /// 舞台布局（对齐 BandVisit_Binguin.cs 里作者定死的坐标）：
        ///   前排：键盘(x-3,西) / 麦克风(x+0,中) / 吉他(x+2,东)
        ///   后排：贝斯(x-3,西) / 架子鼓(x+2,东)（比前排再往后退 3 格）
        /// 观众在舞台北侧、面朝南看 → 画面左右镜像（东 = 画面左）。
        /// </summary>
        private static readonly float[] SlotXFactor = { 0.50f, 0.32f, 0.68f, 0.32f, 0.68f };
        private static readonly bool[] SlotBackRow = { false, false, false, true, true };
    }
}
