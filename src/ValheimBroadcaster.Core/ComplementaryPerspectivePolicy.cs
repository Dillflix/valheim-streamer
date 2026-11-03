using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>A short related-subject view inside one established player scene.
    /// Eligibility is supplied by the runtime's actual actor, geometry, viewpoint
    /// and fresh return-camera checks. It never requests acquisition or travel.</summary>
    public sealed class ComplementaryPerspectivePolicy
    {
        public const double MaximumShownSeconds = 2.75, MaximumWallSeconds = 4, RearmSeconds = 30;
        private double last = -1, primaryShown, readySince = -1, began, cooldownUntil;
        private long scene;
        private string subject = "";
        private bool lastPrimaryShown, lastComplementShown;
        public bool Active { get; private set; }
        public double ShownSeconds { get; private set; }
        public string Reason { get; private set; } = "Awaiting established player context";
        public bool MayPrepare(double now, bool editorialAllowed, ActionPhaseCue phase) => editorialAllowed &&
            !Active && primaryShown >= 6 && now >= cooldownUntil && phase.ComplementaryWindow && !phase.ProtectMoment;

        public bool Update(double now, long sceneId, string subjectId, bool editorialAllowed, ActionPhaseCue phase,
            bool primaryActuallyShown, bool complementaryActuallyShown, bool relationshipValid,
            bool primaryReturnReady, bool complementaryReady)
        {
            if (!Finite(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now));
            bool discontinuity = last >= 0 && (now < last || now - last > 1) || sceneId != scene;
            if (discontinuity) Reset();
            double dt = last >= 0 ? Math.Min(.25, now - last) : 0;
            if (now == last) return Active;
            last = now; scene = sceneId;
            if (primaryActuallyShown && lastPrimaryShown) primaryShown += dt;
            if (Active && complementaryActuallyShown && lastComplementShown) ShownSeconds += dt;
            lastPrimaryShown = primaryActuallyShown;
            lastComplementShown = complementaryActuallyShown;
            bool related = sceneId > 0 && !string.IsNullOrEmpty(subjectId) && relationshipValid;
            if (Active)
            {
                if (!editorialAllowed || !related || subjectId != subject || phase.ProtectMoment ||
                    !primaryReturnReady || !complementaryReady || ShownSeconds >= MaximumShownSeconds || now - began >= MaximumWallSeconds)
                {
                    Active = false; cooldownUntil = now + RearmSeconds; readySince = -1; primaryShown = 0;
                    Reason = "Returning to the player; complementary view ended or evidence changed";
                }
                return Active;
            }
            bool permitted = MayPrepare(now, editorialAllowed, phase) && related && primaryReturnReady && complementaryReady;
            if (!permitted) { readySince = -1; Reason = "Retaining player coverage; complementary view not yet qualified"; return false; }
            if (subject != subjectId || readySince < 0) { subject = subjectId; readySince = now; }
            if (now - readySince < .5) { Reason = "Preparing related-subject view and current player return"; return false; }
            Active = true; began = now; ShownSeconds = 0; lastComplementShown = false;
            Reason = "Brief complementary view; player remains the story owner";
            return true;
        }
        public void Reject(double now)
        {
            Active = false; cooldownUntil = now + RearmSeconds; readySince = -1; primaryShown = 0;
            lastComplementShown = false; Reason = "Complementary candidate rejected; retaining player coverage";
        }
        public void Reset()
        {
            Active = lastPrimaryShown = lastComplementShown = false;
            last = readySince = -1; primaryShown = ShownSeconds = began = cooldownUntil = 0;
            scene = 0; subject = ""; Reason = "Awaiting established player context";
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        /// <summary>Preserve the observed owner/related-subject action axis.
        /// A near-axis source provides no reliable screen-side constraint.</summary>
        public static bool SameSide(AutonomyPosition owner, AutonomyPosition related,
            AutonomyPosition sourceCamera, AutonomyPosition destinationCamera)
        {
            double ax = related.X - owner.X, az = related.Z - owner.Z;
            double length = Math.Sqrt(ax * ax + az * az);
            if (!Finite(length) || length < 1) return false;
            double source = (ax * (sourceCamera.Z - owner.Z) - az * (sourceCamera.X - owner.X)) / length;
            double destination = (ax * (destinationCamera.Z - owner.Z) - az * (destinationCamera.X - owner.X)) / length;
            return Finite(source) && Finite(destination) && (Math.Abs(source) < 1 || Math.Abs(destination) < .5 || source * destination >= 0);
        }
    }
}
