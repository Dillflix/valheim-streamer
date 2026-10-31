using System;
using ValheimBroadcaster.Plugin.Streaming;

internal static class RecoveryChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var policy = new OutputRecoveryPolicy();
        policy.ArmAutoStart(false, 0);
        check(!policy.Desired && !policy.TryBegin(0, true, true), "stream autostart is opt-in");
        policy.ArmAutoStart(true, 0);
        check(policy.Desired && !policy.TryBegin(0, false, true) && !policy.TryBegin(0, true, false), "autostart waits for authorized scene and process cleanup");
        check(policy.TryBegin(0, true, true) && !policy.TryBegin(1, true, true), "only one encoder attempt can be in flight");
        policy.Failed(1, false, true, "receiver unavailable");
        check(policy.RetryIn(1) == 5 && !policy.TryBegin(5.99, true, true) && policy.TryBegin(6, true, true), "first retry has a five-second deadline");
        policy.Publishing(6); policy.Publishing(20);
        policy.Failed(21, false, true, "receiver unavailable");
        check(policy.RetryIn(21) == 10, "brief publishing does not reset outage backoff");
        check(policy.TryBegin(31, true, true), "second retry eventually starts");
        policy.Publishing(31); policy.Publishing(61);
        policy.Failed(62, false, true, "receiver unavailable");
        check(policy.RetryIn(62) == 5 && policy.FailuresInBurst == 1, "stable publishing resets consecutive failure backoff");
        policy.Suspend();
        check(policy.Desired && !policy.InFlight && !policy.TryBegin(100, false, true), "world loss preserves intent without publishing into menus");
        check(policy.TryBegin(100, true, true), "authorized world recovery resumes retained intent");
        policy.RequestStop(); policy.ArmAutoStart(true, 200);
        check(!policy.Desired && policy.ManualSuppressed && !policy.TryBegin(1000, true, true), "explicit stop cannot be undone by autostart or reconnect");
        policy.RequestStart(1000);
        check(!policy.ManualSuppressed && policy.TryBegin(1000, true, true), "explicit on releases manual stop suppression");
        policy.Failed(1001, true, true, "unsupported encoder");
        policy.ArmAutoStart(true, 2000);
        check(policy.Blocked && !policy.TryBegin(2000, true, true), "permanent capability failure blocks repeated startup");
        policy.RequestStart(2000); policy.TryBegin(2000, true, true);
        policy.Failed(2001, false, false, "receiver unavailable");
        check(policy.Blocked && !policy.TryBegin(9999, true, true), "disabled autoretry requires a new manual on after failure");

        policy = new OutputRecoveryPolicy(); policy.RequestStart(0);
        double now = 0;
        double[] expected = { 5, 10, 20, 40, 60, 60, 60, 300, 5 };
        bool bounded = true;
        foreach (double delay in expected)
        {
            bounded &= policy.TryBegin(now, true, true);
            policy.Failed(now, false, true, "outage");
            bounded &= policy.RetryIn(now) == delay;
            now += delay;
        }
        check(bounded, "backoff is capped and every eighth failure adds a five-minute cooldown");
        while (now < 172800)
        {
            if (!policy.TryBegin(now, true, true)) throw new Exception("Lost retry deadline");
            policy.Failed(now, false, true, "outage");
            now += policy.RetryIn(now);
        }
        check(policy.Attempts < 2600 && policy.FailuresInBurst <= 8, "48-hour receiver outage remains bounded without unbounded queues/history");
        bool rejected = false;
        try { policy.RequestStart(double.NaN); } catch (ArgumentOutOfRangeException) { rejected = true; }
        check(rejected, "recovery rejects invalid clock input");
    }
}
