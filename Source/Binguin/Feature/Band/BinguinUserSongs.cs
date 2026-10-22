// ============================================================================
// 冰鹅族【自定义歌曲库】（2026-10-17 作者需求）
//
// ★ 作者原话：
//   「做一个在 mod 选项可以放置自己歌的按键，点击后可以打开歌曲库，
//     玩家可以把自己想听的歌放进去，然后在演唱会播放」
//
// ★ 工作方式
//   1. 玩家把 .mp3 / .ogg / .wav 丢进：
//        <模组目录>\Sounds\ConcertSongs\
//   2. 启动游戏时本类扫描该文件夹，**运行时动态生成**三个 Def：
//        SoundDef      （onCamera 一发的音频，兜底用）
//        Verse.SongDef （背景音乐轨道用，演唱会走这条）
//        Binguin_SongDef（曲目条目，出现在选曲界面）
//   3. 演唱会选曲界面就会多出这些歌。
//
// ★ 为什么必须**启动时**扫描（1.7 实证）
//   `AudioGrain_Clip` / `AudioGrain_Folder` 最终都走
//     Verse.ContentFinder<AudioClip>.Get(path)
//   而 ContentFinder 的内容索引是在**加载期**由 ModContentLoader 扫盘建立的
//   ⇒ 游戏运行中往文件夹里新加的文件，当次运行**不会**被发现，
//     必须重启游戏（这一点会写进设置界面提示）。
//
// ★ 关键字段（1.7 反射确认）
//   Verse.SoundDef        : context / maxSimultaneous / slot / sustain / subSounds
//   Verse.Sound.SubSoundDef: onCamera / grains / volumeRange / pitchRange / gameSpeedRange
//   Verse.Sound.AudioGrain_Clip: clipPath
//   Verse.SongDef         : clipPath / volume / playOnMap / commonality / tense
//   DefDatabase<T>.Add(T) 与 SoundDef.ResolveReferences() 都是 public
//
// ★ 为什么不新建 XML：文件名是玩家自定的，XML 里不可能预先列出。
//   ⇒ 只能在运行期造 Def。造完必须调 `ResolveReferences()`，
//     否则 SubSoundDef.resolvedGrains 是空的（TryPlay 会报 "No resolved grains"）。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using RimWorld;
using Verse;
using Verse.Sound;
using Binguin.Helper;

namespace Binguin.Feature.Band
{
    /// <summary>扫描玩家的自定义歌曲文件夹，运行时生成曲目 Def。</summary>
    [StaticConstructorOnStartup]
    public static class BinguinUserSongs
    {
        /// <summary>支持的音频扩展名（1.7 的 AcceptableExtensionsAudio 子集）。</summary>
        private static readonly string[] Exts = new string[] { ".mp3", ".ogg", ".wav" };

        /// <summary>已成功载入的自定义曲目名（用于设置界面显示）。</summary>
        public static readonly List<string> LoadedNames = new List<string>();

        /// <summary>本模组的歌曲库文件夹绝对路径（找不到则为空串）。</summary>
        public static string FolderPath = "";

        /// <summary>上次扫描的结果摘要。</summary>
        public static string LastResult = "Binguin_Interface_054".Translate().ToString();

        static BinguinUserSongs()
        {
            try
            {
                Scan();
            }
            catch (Exception e)
            {
                LastResult = "Binguin_Interface_055".Translate().ToString() + e.Message;
                BinguinLogUtility.Log("自定义歌曲库扫描失败：" + e,
                    severity: 1, isDebug: false);
            }
        }

        /// <summary>解析模组根目录（本 DLL 在 <模组>/Assemblies/ 下）。</summary>
        public static string ResolveFolder()
        {
            try
            {
                string asmPath = Assembly.GetExecutingAssembly().Location;
                if (asmPath.NullOrEmpty()) return "";
                string asmDir = Path.GetDirectoryName(asmPath);
                if (asmDir.NullOrEmpty()) return "";
                string modRoot = Path.GetDirectoryName(asmDir);
                if (modRoot.NullOrEmpty()) return "";
                return Path.Combine(Path.Combine(modRoot, "Sounds"), "ConcertSongs");
            }
            catch (Exception)
            {
                return "";
            }
        }

        /// <summary>重新扫描并生成 Def（设置界面的「重新扫描」按钮调用）。</summary>
        public static void Rescan()
        {
            Scan();
            BinguinConcertSongs.ResetCache();
        }

        private static void Scan()
        {
            LoadedNames.Clear();
            FolderPath = ResolveFolder();
            if (FolderPath.NullOrEmpty() || !Directory.Exists(FolderPath))
            {
                LastResult = "Binguin_Interface_056".Translate().ToString();
                BinguinLogUtility.Log("自定义歌曲库目录不存在：" + FolderPath,
                    severity: 1, isDebug: false);
                return;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(FolderPath);
            }
            catch (Exception e)
            {
                LastResult = "Binguin_Interface_057".Translate().ToString() + e.Message;
                return;
            }

            int added = 0;
            int skipped = 0;
            for (int i = 0; i < files.Length; i++)
            {
                string full = files[i];
                string ext = Path.GetExtension(full);
                if (ext.NullOrEmpty()) { skipped++; continue; }
                ext = ext.ToLowerInvariant();
                bool ok = false;
                for (int k = 0; k < Exts.Length; k++)
                {
                    if (ext == Exts[k]) { ok = true; break; }
                }
                if (!ok) { skipped++; continue; }

                string nameNoExt = Path.GetFileNameWithoutExtension(full);
                string relPath = "ConcertSongs/" + nameNoExt;

                // ★ 去重：XML 里已经定义过的曲目（MyGO 那 4 首）也放在同一个文件夹，
                //   必须跳过，否则玩家会在选曲界面看到两份同名歌。
                //   判据用 **clipPath**（比 defName 可靠：玩家可能改了文件名）。
                if (AlreadyDefined(relPath))
                {
                    continue;
                }

                try
                {
                    if (CreateOne(nameNoExt, relPath, full))
                    {
                        added++;
                        LoadedNames.Add(nameNoExt);
                    }
                    else
                    {
                        skipped++;
                    }
                }
                catch (Exception e)
                {
                    skipped++;
                    BinguinLogUtility.Log("生成自定义曲目「" + nameNoExt + "」失败："
                        + e.Message, severity: 1, isDebug: false);
                }
            }

            LastResult = "Binguin_Interface_058".Translate().ToString() + added + "Binguin_Interface_059".Translate().ToString() + skipped + "Binguin_Interface_060".Translate().ToString();
            BinguinLogUtility.Log("自定义歌曲库：" + LastResult + "（目录 " + FolderPath + "）",
                isDebug: false);
        }

        /// <summary>这个 clipPath 是否已经被某个已有曲目用掉了。</summary>
        private static bool AlreadyDefined(string relPath)
        {
            try
            {
                List<Binguin_SongDef> all = DefDatabase<Binguin_SongDef>.AllDefsListForReading;
                if (all == null) return false;
                for (int i = 0; i < all.Count; i++)
                {
                    Binguin_SongDef d = all[i];
                    if (d == null || d.musicDef.NullOrEmpty()) continue;
                    SongDef m = DefDatabase<SongDef>.GetNamedSilentFail(d.musicDef);
                    if (m != null && m.clipPath == relPath) return true;
                }
            }
            catch (Exception) { }
            return false;
        }

        private static bool CreateOne(string nameNoExt, string relPath, string fullPath)
        {
            // ① 先确认音频真的能被 ContentFinder 找到（否则造出来也没声音）
            AudioClip clip = null;
            try
            {
                clip = ContentFinder<AudioClip>.Get(relPath, false);
            }
            catch (Exception) { }
            if (clip == null)
            {
                BinguinLogUtility.Log("跳过「" + nameNoExt
                    + "」：ContentFinder 找不到音频（可能是格式不支持，或文件是本次运行中新加的 —— 需重启游戏）",
                    severity: 1, isDebug: false);
                return false;
            }

            string key;
            using (System.Security.Cryptography.SHA256 hash = System.Security.Cryptography.SHA256.Create())
            {
                byte[] bytes = hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(relPath.Replace('\\', '/').ToLowerInvariant()));
                key = Sanitize(nameNoExt) + "_" + BitConverter.ToString(bytes).Replace("-", "").Substring(0, 16);
            }

            // ② SoundDef（onCamera 一发，兜底通道）
            SoundDef snd = new SoundDef();
            snd.defName = "Binguin_UserClip_" + key;
            snd.label = nameNoExt;
            snd.context = SoundContext.MapOnly;
            snd.sustain = false;
            snd.maxSimultaneous = 1;
            snd.slot = "";
            SubSoundDef sub = new SubSoundDef();
            sub.onCamera = true;
            sub.muteWhenPaused = false;
            sub.volumeRange = new FloatRange(1f, 1f);
            sub.pitchRange = new FloatRange(1f, 1f);
            sub.gameSpeedRange = new IntRange(0, 4);
            sub.grains = new List<AudioGrain>();
            AudioGrain_Clip grain = new AudioGrain_Clip();
            grain.clipPath = relPath;
            sub.grains.Add(grain);
            sub.parentDef = snd;
            snd.subSounds = new List<SubSoundDef>();
            snd.subSounds.Add(sub);
            snd.ResolveReferences();
            DefDatabase<SoundDef>.Add(snd);

            // ③ Verse.SongDef（背景音乐轨道，演唱会主用）
            SongDef music = new SongDef();
            music.defName = "Binguin_UserMusic_" + key;
            music.clipPath = relPath;
            music.volume = 0.55f;
            music.playOnMap = true;
            music.commonality = 0f;
            music.tense = false;
            music.allowedTimeOfDay = TimeOfDay.Any;
            music.clip = clip;
            DefDatabase<SongDef>.Add(music);

            // ④ Binguin_SongDef（选曲界面条目）
            Binguin_SongDef song = new Binguin_SongDef();
            song.defName = "Binguin_UserSong_" + key;
            song.label = nameNoExt;
            song.description = "Binguin_Interface_061".Translate().ToString() + Path.GetFileName(fullPath);
            song.style = "Binguin_Interface_062".Translate().ToString();
            song.durationTicks = EstimateTicks(fullPath, clip);
            song.moodOffset = 0f;
            song.requiredArtisticLevel = 0;
            song.soundDef = snd.defName;
            song.musicDef = music.defName;
            DefDatabase<Binguin_SongDef>.Add(song);

            BinguinLogUtility.Log("已载入自定义曲目「" + nameNoExt + "」"
                + "（时长约 " + (song.durationTicks / 60f).ToString("0") + " 秒）",
                isDebug: false);
            return true;
        }

        /// <summary>
        /// 估算时长（tick）。
        /// 优先用 AudioClip.length；拿不到就按文件大小估（假设 192 kbps）。
        /// </summary>
        private static int EstimateTicks(string fullPath, AudioClip clip)
        {
            float sec = 0f;
            try
            {
                if (clip != null) sec = clip.length;
            }
            catch (Exception) { sec = 0f; }

            if (sec <= 1f)
            {
                try
                {
                    long bytes = new FileInfo(fullPath).Length;
                    sec = bytes * 8f / 192000f;      // 192 kbps
                }
                catch (Exception) { sec = 180f; }
            }
            if (sec < 5f) sec = 5f;
            if (sec > 1800f) sec = 1800f;           // 最多 30 分钟
            return Mathf.RoundToInt(sec * 60f);
        }

        /// <summary>把文件名清洗成合法 defName 片段。</summary>
        private static string Sanitize(string s)
        {
            if (s.NullOrEmpty()) return "X";
            System.Text.StringBuilder sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
                else sb.Append('_');
            }
            return sb.ToString();
        }

        /// <summary>在系统文件管理器里打开歌曲库文件夹。</summary>
        public static void OpenFolder()
        {
            string p = FolderPath.NullOrEmpty() ? ResolveFolder() : FolderPath;
            if (p.NullOrEmpty())
            {
                Messages.Message("Binguin_UserSongs_NoFolder".Translate(),
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            try
            {
                if (!Directory.Exists(p)) Directory.CreateDirectory(p);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + p + "\"");
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("打开文件夹失败：" + e.Message, severity: 1, isDebug: false);
                Messages.Message("Binguin_Interface_063".Translate().ToString() + p, MessageTypeDefOf.NeutralEvent, false);
            }
        }
    }
}
