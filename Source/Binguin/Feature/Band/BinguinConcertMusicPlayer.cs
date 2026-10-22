using System;
using Binguin.Helper;
using RimWorld;
using UnityEngine;
using Verse;

namespace Binguin.Feature.Band
{
    // 当前曲目按索引保存，读档后重播该首。播放状态只在主线程的帧更新中访问。
    public class BinguinConcertMusicPlayer : IExposable
    {
        private int songIndex;
        private SongDef playingMusic;
        private float fallbackEndsAt = -1f;

        public void ExposeData()
        {
            Scribe_Values.Look(ref songIndex, "songIndex");
        }

        // 返回 true 表示整个歌单播放完毕。背景音乐依据音轨实际完成状态换曲，
        // SoundDef 后备依据真实秒数计时，均不随游戏倍速提前截断。
        public bool Update(BinguinConcertBooking playlist)
        {
            if (playlist == null || playlist.songs.NullOrEmpty()) return true;
            MusicManagerPlay manager = Find.MusicManagerPlay;
            if (playingMusic != null)
            {
                if (manager == null) return false;
                if (manager.CurrentSong == playingMusic && manager.IsPlaying) return false;
                playingMusic = null;
                songIndex++;
            }
            if (fallbackEndsAt >= 0f)
            {
                if (Time.realtimeSinceStartup < fallbackEndsAt) return false;
                fallbackEndsAt = -1f;
                songIndex++;
            }
            if (songIndex >= playlist.songs.Count) return true;

            Binguin_SongDef song = playlist.songs[songIndex];
            if (song == null) { songIndex++; return false; }
            try
            {
                SongDef music = song.musicDef.NullOrEmpty() ? null
                    : DefDatabase<SongDef>.GetNamedSilentFail(song.musicDef);
                if (music != null && music.clip != null)
                {
                    if (manager == null) return false; // 音乐管理器尚未就绪，不消耗曲目。
                    manager.ForcePlaySong(music, false); // false：尊重玩家音乐音量。
                    playingMusic = music;
                    BinguinLogUtility.Log("演唱会背景音乐：" + music.defName, isDebug: false);
                    return false;
                }

                // 只有背景音乐不可用时才走音效后备，避免同一首歌重叠播放两遍。
                SoundDef fallback = song.soundDef.NullOrEmpty() ? null
                    : DefDatabase<SoundDef>.GetNamedSilentFail(song.soundDef);
                if (fallback != null && playlist.Map != null)
                {
                    Verse.Sound.SoundStarter.PlayOneShotOnCamera(fallback, playlist.Map);
                    fallbackEndsAt = Time.realtimeSinceStartup + song.EffectiveDurationTicks / 60f;
                    BinguinLogUtility.WarningOnce("曲目 " + song.defName
                        + " 的背景音乐不可用，改用 SoundDef 后备播放。", song.shortHash + 2801000);
                    return false;
                }
                BinguinLogUtility.WarningOnce("曲目 " + song.defName
                    + " 没有可播放的音频，跳过该曲目。", song.shortHash + 2802000);
            }
            catch (Exception e)
            {
                BinguinLogUtility.WarningOnce("播放曲目 " + song.defName + " 失败：" + e,
                    song.shortHash + 2803000);
            }
            songIndex++;
            return false;
        }
    }
}
