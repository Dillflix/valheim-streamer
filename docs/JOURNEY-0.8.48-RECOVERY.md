# Journey interruption investigation — 0.8.48

## Evidence from the supplied 0.8.47 recording

The relevant run requested 5,000 metres at 12 m/s. Journey 63 selected a real
Black Forest/coast destination about 4,939 metres away. It went on air at
166.0169 seconds. Captured, render-matched positions cover approximately
138.6 metres before the final travelling sample; this was not a completed
five-kilometre journey.

The decisive sequence in the synchronized trace is:

| Time in run | Observation |
| --- | --- |
| 179.4052 s | One queued section remained, with 33.94 metres of fresh runway. |
| 179.6386 s | Revalidation rejected the queued camera footprint below water clearance, requiring a 0.8 metre lift. |
| Immediately afterward | The queue was discarded and the current section committed to an ending. |
| 181.0553 s | Prepared ending 77 was admitted. |
| 187.0730 s | The Journey ended as blocked because its continuation was unavailable at the braking point. |

Earlier preparation failures also include a rising surface at a preserved
incoming tangent and generic path/composition bounds. The old trace does not
identify which argument caused those generic failures. They must not be
reported as proven duration failures. The fresh water-clearance rejection
does establish the final queue-loss/ending decision.

## Root causes and changes

**A route was certified against a transient water surface.** A trough could
permit a path which the next crest invalidated. Journey preparation now uses
the loaded water volume's base, surface offset and depth, together with an
upper wave envelope derived from Valheim 1.0.21's wave amplitudes. It covers
the ordinary weather-intensity range and any larger currently observed wind
intensity. Native live clearance remains authoritative. Missing measurements
do not become a guessed sea level. Ordinary Scenic routes retain their
existing surface policy; Journey sections and their prepared endings retain
the envelope through fitting and renewal.

**A local route failure could discard the mission.** A section's permission
to run and the Journey's destination are distinct. Contradicted geometry must
stop being eligible immediately. Repair must retain useful certified work,
renew transient visibility evidence without extending its deadline on every
poll, and independently validate any replacement departure before resuming.
The implementation and regression tests distinguish this from continuing
through a known collision or presenting unvalidated pixels.

The recovery owner retains the original Journey identity, itinerary, progress
and runtime allowance. A prepared ending can bridge the interruption for up
to nine seconds, after which ordinary or independent Scenic coverage owns the
picture while repair continues. Expiry of that ending does not end the
mission. A blocked live movement or an unavailable prepared ending enters
the same local repair path. A new departure needs two connected, currently
validated sections and useful readable coverage before it can resume.

Physical camera/surface/vegetation containment invalidates the affected queue
suffix immediately; earlier certified sections remain. Visibility-only
Focus/Foliage failures immediately remove admission permission but allow one
fixed two-second revalidation interval. Repeated failures or unknown readings
cannot renew that interval. Failure aggregation retains physical evidence
even if a later sample reports only a framing problem. This is evidence-aware
repair, not a forecast of every foliage shader or moving obstacle.

Each search group has at most three failed attempts. Another materially
different observed viewpoint or a later bounded fresh-evaluation interval
can justify a retry; polling cannot reset attempts every frame. A local scene
generation change discards old proposals without discarding the world-scoped
destination. Actual world/player/operator/catalogue revocation, the original
Journey runtime limit, or the existing missing-picture recovery deadline can
still end the mission. No speculative route receives loading-carrier
authority. A repair cut is deliberate and does not earn travel credit; this
change does not promise an uninterrupted five-kilometre shot.

**Rendered movement earned zero distance.** The next update consumed a
previous render using a camera context that had already expired. Its scene
identity became zero, so valid capture samples failed the strict matching
policy. The successful solve now retains an immutable presentation identity
for the capture it owns. Tests include the supplied frame identities, expired
contexts, scene replacement and repeated consumption. A local cut still earns
no travel credit.

**Short loaded sections had inconsistent timing constraints.** Eight-to-twelve
metre windows at 12 m/s may legitimately take less than a second. Journey
duration validation now supports those windows while retaining the ordinary
shot minimum. Centimetre-scale trailing control points are absorbed into the
preceding segment, and construction failures report the actual failing
argument rather than one generic composition error.

## Acceptance still required in Valheim

Build, deterministic policy tests and real-assembly inspection do not execute
Unity physics, world streaming or the game renderer. Repeat the existing
`broadcast journey distance 5000`, `broadcast journey speed 12`,
`broadcast journey start` sequence with synchronized capture enabled.

Verify that normal ocean crests do not repeatedly invalidate prepared routes;
that a local obstruction produces an explicitly diagnosed repair toward the
same destination; and that scene changes, operator stop and player priority
still revoke the appropriate ownership. Check forest, shoreline and open-sea
runs, including actual footage rather than just plan counters. A successful
local test of the repair policy is not evidence that a five-kilometre runtime
Journey has completed.
