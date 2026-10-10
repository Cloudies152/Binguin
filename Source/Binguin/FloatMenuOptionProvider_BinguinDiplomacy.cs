// ============================================================================
// 冰鹅族外交官——交谈入口（RimWorld 1.6）
//
// ★ 2026-08-22 用户需求：外交官到达后【不再自动弹出共存意向选择信】，
//   玩家须选中一名殖民者，右键点击外交官，殖民者走到她面前交谈，
//   才进入共存意向事件。
//
// 1.6 自带可扩展菜单系统 RimWorld.FloatMenuOptionProvider：FloatMenuMakerMap
// 启动时收集其全部非抽象子类（GenTypes.AllSubclassesNonAbstract + Activator），
// 选中殖民者右键点击外交官时按“每个被点击的 pawn”回调：
//   TargetPawnValid(clickedPawn) → GetOptionsFor(clickedPawn) →
//   基类迭代器仅调用 GetSingleOptionFor(clickedPawn, context) 一次。
// 本类只覆盖三个抽象属性 + 返回一个右键选项即可。
// ============================================================================

using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Binguin
{
    public class FloatMenuOptionProvider_BinguinDiplomacy : FloatMenuOptionProvider
    {
        // 允许被征召与未被征召的殖民者发起交谈（抽象属性，必须覆盖；
        // 基类成员是 protected，覆盖不能提升可访问性）
        protected override bool Drafted
        {
            get { return true; }
        }

        protected override bool Undrafted
        {
            get { return true; }
        }

        // 允许多选殖民者时显示
        protected override bool Multiselect
        {
            get { return true; }
        }

        protected override FloatMenuOption GetSingleOptionFor(Pawn clickedPawn, FloatMenuContext context)
        {
            try
            {
                GameComponent_BinguinDiplomacy gc = Current.Game != null
                    ? Current.Game.GetComponent<GameComponent_BinguinDiplomacy>() : null;
                if (gc == null || !gc.IsDiplomatWaiting(clickedPawn))
                {
                    return null; // 不是“正在等候答复”的外交官
                }

                // 从当前选中的殖民者里挑一个可用的执行交谈
                Pawn actor = null;
                List<Pawn> selected = context.allSelectedPawns;
                if (selected != null)
                {
                    for (int i = 0; i < selected.Count; i++)
                    {
                        Pawn p = selected[i];
                        if (p == null || p.Destroyed || !p.Spawned || p == clickedPawn)
                        {
                            continue;
                        }
                        if (!p.IsColonist || p.Downed || p.InMentalState || p.IsPrisonerOfColony)
                        {
                            continue;
                        }
                        actor = p;
                        break;
                    }
                }
                if (actor == null)
                {
                    return null;
                }

                FloatMenuOption option = new FloatMenuOption("Binguin_FloatMenuOptionProviderDiplomacy_01".Translate(),
                    delegate
                    {
                        try
                        {
                            JobDef jd = DefDatabase<JobDef>.GetNamedSilentFail("Binguin_DiplomatTalk");
                            if (jd == null)
                            {
                                Messages.Message("Binguin_FloatMenuOptionProviderDiplomacy_02".Translate(), MessageTypeDefOf.RejectInput, false);
                                return;
                            }
                            if (!gc.IsDiplomatWaiting(clickedPawn))
                            {
                                Messages.Message("Binguin_FloatMenuOptionProviderDiplomacy_03".Translate(), MessageTypeDefOf.RejectInput, false);
                                return;
                            }
                            Job job = new Job(jd, clickedPawn);
                            actor.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                        }
                        catch (Exception e)
                        {
                            Log.Warning("[冰鹅族] 派遣交谈任务异常：" + e.Message);
                        }
                    });
                option.autoTakeable = false;

                // 到不了外交官身边时置灰
                if (!ReachabilityUtility.CanReach(actor, clickedPawn,
                        PathEndMode.Touch, Danger.Deadly, false, false, TraverseMode.ByPawn))
                {
                    option.Disabled = true;
                }
                return option;
            }
            catch (Exception e)
            {
                Log.Warning("[冰鹅族] 外交官右键菜单异常：" + e.Message);
                return null;
            }
        }
    }
}
