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

namespace Binguin
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
            DiaOption accept = new DiaOption("Binguin_ChoiceLetterDiplomacy_01".Translate());
            accept.action = delegate
            {
                GameComponent_BinguinDiplomacy comp = Current.Game.GetComponent<GameComponent_BinguinDiplomacy>();
                if (comp != null)
                {
                    comp.HandleResponse(GameComponent_BinguinDiplomacy.RespAccepted);
                }
            };
            optionList.Add(accept);

            DiaOption neutral = new DiaOption("Binguin_ChoiceLetterDiplomacy_02".Translate());
            neutral.action = delegate
            {
                GameComponent_BinguinDiplomacy comp = Current.Game.GetComponent<GameComponent_BinguinDiplomacy>();
                if (comp != null)
                {
                    comp.HandleResponse(GameComponent_BinguinDiplomacy.RespNeutral);
                }
            };
            optionList.Add(neutral);

            DiaOption refuse = new DiaOption("Binguin_ChoiceLetterDiplomacy_03".Translate());
            refuse.action = delegate
            {
                GameComponent_BinguinDiplomacy comp = Current.Game.GetComponent<GameComponent_BinguinDiplomacy>();
                if (comp != null)
                {
                    comp.HandleResponse(GameComponent_BinguinDiplomacy.RespRefused);
                }
            };
            optionList.Add(refuse);
        }
    }
}
