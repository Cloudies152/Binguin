// ============================================================================
// 冰鹅族外交选择信件（RimWorld 1.6）
//
// 1.6 的 ChoiceLetter 是抽象类：Choices 是抽象属性（IEnumerable<DiaOption>），
// 旧版的 choices 列表与 LetterMaker 均已移除。本类在构造/读档时重建两个固定选项，
// 按钮动作通过 Game.GetComponent<T>() 回调外交组件。
// ============================================================================

using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Binguin.Helper;

namespace Binguin.Feature.Diplomacy
{
    public class ChoiceLetter_BinguinDiplomacy : ChoiceLetter
    {
        private List<DiaOption> optionList = new List<DiaOption>();

        public ChoiceLetter_BinguinDiplomacy()
        {
            BuildOptions();
        }

        public override IEnumerable<DiaOption> Choices
        {
            get { return optionList; }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            // 按钮动作是委托，无法序列化；读档后重建（构造函数在反序列化时也会执行，双重保障）
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                BuildOptions();
            }
        }

        private void BuildOptions()
        {
            optionList.Clear();

            // 2026-08-22：三个共存意向选项——
            //   愿意共存 +50（立即）；保持中立 好感不变；
            //   无意共存 -75（等外交官走出地图边缘才结算，见 HandleResponse/DepartDiplomat）
            //
            // ★★★ 2026-10-22 修作者反馈「点选项后就死机」：
            //   `DiaOption.Activate()` 只在 **resolveTree == true** 时才
            //   `OwningDialog.Close()`（IL 实证）；而 `DiaOption` 的构造函数
            //   默认 resolveTree = false，我们三个选项又只填了 `action`。
            //   结果：点完按钮，对话窗口**永远不关**、游戏一直暂停
            //   —— 看起来就是"死机"（其实进程还活着）。
            //   ⇒ 三个选项都设 resolveTree = true；再按原版惯例把信从信箱移除。
            DiaOption accept = new DiaOption("Binguin_ChoiceLetterDiplomacy_01".Translate());
            accept.resolveTree = true;
            accept.action = delegate { Resolve(GameComponent_BinguinDiplomacy.RespAccepted); };
            optionList.Add(accept);

            DiaOption neutral = new DiaOption("Binguin_ChoiceLetterDiplomacy_02".Translate());
            neutral.resolveTree = true;
            neutral.action = delegate { Resolve(GameComponent_BinguinDiplomacy.RespNeutral); };
            optionList.Add(neutral);

            DiaOption refuse = new DiaOption("Binguin_ChoiceLetterDiplomacy_03".Translate());
            refuse.resolveTree = true;
            refuse.action = delegate { Resolve(GameComponent_BinguinDiplomacy.RespRefused); };
            optionList.Add(refuse);
        }

        /// <summary>选项点击后的统一处理：回外交组件 + 把信从信箱移除（原版 ChoiceLetter 都这么做）。</summary>
        private void Resolve(int kind)
        {
            try
            {
                BinguinLogUtility.Log("共存意向选项已点击：kind=" + kind, isDebug: false);
                if (Current.Game != null)
                {
                    GameComponent_BinguinDiplomacy comp =
                        Current.Game.GetComponent<GameComponent_BinguinDiplomacy>();
                    if (comp != null)
                    {
                        comp.HandleResponse(kind);
                    }
                }
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("外交选项处理异常：" + e.Message, severity: 1, isDebug: false);
            }
            finally
            {
                try
                {
                    if (Find.LetterStack != null)
                    {
                        Find.LetterStack.RemoveLetter(this);
                    }
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
