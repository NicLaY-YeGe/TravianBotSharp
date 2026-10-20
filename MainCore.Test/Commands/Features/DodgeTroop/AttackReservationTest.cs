using MainCore.Commands.Features.DodgeTroop;

namespace MainCore.Test.Commands.Features.DodgeTroop
{
    public class AttackReservationTest
    {
        private static readonly DateTime Now = new(2026, 10, 19, 16, 10, 0);
        private static readonly DateTime Impact = new(2026, 10, 19, 16, 19, 43);

        private static ReservationQueueEntry Normal(DateTime at) => new(at, false);
        private static ReservationQueueEntry Exempt(DateTime at) => new(at, true);
        private static AttackReservation Reservation(int lead = 240) => new(Impact, lead);

        [Fact]
        public void RandomLead_StaysInThreeToFiveMinutes()
        {
            var random = new Random(7);
            for (var i = 0; i < 500; i++)
            {
                var lead = AttackReservationRules.RandomLeadSeconds(random);
                (lead >= 180).ShouldBeTrue();
                (lead <= 300).ShouldBeTrue();
            }
        }

        [Fact]
        public void Window_StartsAtLeadAndEndsAfterImpact()
        {
            var r = Reservation(240);
            r.Covers(Impact.AddSeconds(-241)).ShouldBeFalse();
            r.Covers(Impact.AddSeconds(-240)).ShouldBeTrue();
            r.Covers(Impact).ShouldBeTrue();
            r.Covers(Impact.AddSeconds(14)).ShouldBeTrue();
            r.Covers(Impact.AddSeconds(15)).ShouldBeFalse();
        }

        [Fact]
        public void NoReservation_OnlyHeadAndOnlyWhenDue()
        {
            var queue = new List<ReservationQueueEntry> { Normal(Now), Exempt(Now) };
            AttackReservationRules.SelectIndex(queue, Now, []).ShouldBe(0);

            var future = new List<ReservationQueueEntry> { Normal(Now.AddSeconds(5)) };
            AttackReservationRules.SelectIndex(future, Now, []).ShouldBe(-1);
        }

        [Fact]
        public void BeforeWindow_BehavesLikeNoReservation()
        {
            var queue = new List<ReservationQueueEntry> { Normal(Now), Exempt(Now) };
            // 9 min 43 s before impact, window opens 4 min before.
            AttackReservationRules.SelectIndex(queue, Now, [Reservation()]).ShouldBe(0);
        }

        [Fact]
        public void InsideWindow_NormalHeadIsHeldBack_ExemptRuns()
        {
            var inside = Impact.AddSeconds(-120);
            var queue = new List<ReservationQueueEntry> { Normal(inside.AddSeconds(-30)), Normal(inside.AddSeconds(-20)), Exempt(inside.AddSeconds(-10)) };
            AttackReservationRules.SelectIndex(queue, inside, [Reservation()]).ShouldBe(2);
        }

        [Fact]
        public void InsideWindow_OnlyNormalTasks_NothingRuns()
        {
            var inside = Impact.AddSeconds(-120);
            var queue = new List<ReservationQueueEntry> { Normal(inside.AddSeconds(-30)), Normal(inside.AddSeconds(-5)) };
            AttackReservationRules.SelectIndex(queue, inside, [Reservation()]).ShouldBe(-1);
        }

        [Fact]
        public void InsideWindow_ExemptNotYetDue_NothingRuns()
        {
            var inside = Impact.AddSeconds(-120);
            var queue = new List<ReservationQueueEntry> { Normal(inside.AddSeconds(-30)), Exempt(inside.AddSeconds(90)) };
            AttackReservationRules.SelectIndex(queue, inside, [Reservation()]).ShouldBe(-1);
        }

        [Fact]
        public void AfterWindow_HeldBackTaskRunsAgain()
        {
            var after = Impact.AddSeconds(16);
            var queue = new List<ReservationQueueEntry> { Normal(Impact.AddSeconds(-200)), Exempt(Impact.AddSeconds(-30)) };
            AttackReservationRules.SelectIndex(queue, after, [Reservation()]).ShouldBe(0);
        }

        private static ReservationQueueEntry Priority(DateTime at) => new(at, true, true);

        [Fact]
        public void NoReservation_DuePriorityJumpsAheadOfDueNormalHead()
        {
            // The 2026-10-20 11:30 case: held-back tasks (older times) sit in front of a due Recall.
            var queue = new List<ReservationQueueEntry> { Normal(Now.AddSeconds(-60)), Normal(Now.AddSeconds(-30)), Priority(Now.AddSeconds(-5)) };
            AttackReservationRules.SelectIndex(queue, Now, []).ShouldBe(2);
        }

        [Fact]
        public void NoReservation_PriorityNotYetDue_HeadRuleStillApplies()
        {
            var queue = new List<ReservationQueueEntry> { Normal(Now.AddSeconds(-30)), Priority(Now.AddSeconds(40)) };
            AttackReservationRules.SelectIndex(queue, Now, []).ShouldBe(0);
        }

        [Fact]
        public void AfterWindow_DueRecallRunsBeforeHeldBackTasks()
        {
            var after = Impact.AddSeconds(30);
            var queue = new List<ReservationQueueEntry> { Normal(Impact.AddSeconds(-200)), Normal(Impact.AddSeconds(-150)), Priority(Impact.AddSeconds(5)) };
            AttackReservationRules.SelectIndex(queue, after, [Reservation()]).ShouldBe(2);
        }

        [Fact]
        public void InsideWindow_PriorityBeatsOtherExempt()
        {
            var inside = Impact.AddSeconds(-100);
            var queue = new List<ReservationQueueEntry> { Exempt(inside.AddSeconds(-20)), Priority(inside.AddSeconds(-10)) };
            AttackReservationRules.SelectIndex(queue, inside, [Reservation()]).ShouldBe(1);
        }

        [Fact]
        public void TwoDuePriority_KeepsQueueOrder()
        {
            var queue = new List<ReservationQueueEntry> { Normal(Now.AddSeconds(-30)), Priority(Now.AddSeconds(-20)), Priority(Now.AddSeconds(-10)) };
            AttackReservationRules.SelectIndex(queue, Now, []).ShouldBe(1);
        }

        [Fact]
        public void Store_SameAttackReRead_KeepsLead()
        {
            AttackReservationStore.ClearAll();
            var (first, isNew) = AttackReservationStore.Register(1, 10, Impact, Now, new Random(1));
            isNew.ShouldBeTrue();

            var (second, isNewAgain) = AttackReservationStore.Register(1, 10, Impact.AddSeconds(2), Now.AddSeconds(30), new Random(99));
            isNewAgain.ShouldBeFalse();
            second.LeadSeconds.ShouldBe(first.LeadSeconds);
            second.ImpactAt.ShouldBe(Impact.AddSeconds(2));
        }

        [Fact]
        public void Store_DifferentImpact_IsANewAttack()
        {
            AttackReservationStore.ClearAll();
            AttackReservationStore.Register(1, 10, Impact, Now, new Random(1));

            var (_, isNew) = AttackReservationStore.Register(1, 10, Impact.AddMinutes(30), Now.AddSeconds(30), new Random(2));
            isNew.ShouldBeTrue();
        }

        [Fact]
        public void Store_GetActive_IsPerAccount_AndDropsExpired()
        {
            AttackReservationStore.ClearAll();
            AttackReservationStore.Register(1, 10, Impact, Now, new Random(1));
            AttackReservationStore.Register(2, 20, Impact, Now, new Random(1));

            AttackReservationStore.GetActive(1, Now).Count.ShouldBe(1);
            AttackReservationStore.GetActive(3, Now).Count.ShouldBe(0);
            AttackReservationStore.GetActive(1, Impact.AddSeconds(20)).Count.ShouldBe(0);
        }

        [Fact]
        public void Store_Clear_RemovesReservation()
        {
            AttackReservationStore.ClearAll();
            AttackReservationStore.Register(1, 10, Impact, Now, new Random(1));
            AttackReservationStore.Clear(1, 10);

            AttackReservationStore.GetActive(1, Now).Count.ShouldBe(0);
        }
    }
}
