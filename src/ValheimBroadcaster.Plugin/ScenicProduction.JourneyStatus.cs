using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

internal sealed partial class ScenicProduction
{
    internal string JourneyPictureLabel => JourneyOperatorStatus.DescribePicture(journeyTravel.State,
        JourneyOwnsPicture, journeyRepairing, journeyHeld, journeyTravel.CanAdvance,
        journeyRunway.EffectiveCruise, journey.TravelMetres, journeyTravel.NoProgressSeconds);

    // Read mission and picture ownership independently. A retained Journey
    // frame can remain a fresh live render while physical advance is stopped.
    internal JourneyOperatorStatus JourneyOperator
    {
        get
        {
            // A new request must not display the previous mission's destination
            // or distance while its own itinerary has not been selected yet.
            bool selecting = requestedJourney && !journeyTravel.Active;
            return new JourneyOperatorStatus(JourneyOwnsTravel,
                selecting ? JourneyTravelState.Departure : journeyTravel.State,
                JourneyOwnsPicture, journeyRepairing, journeyHeld, journeyTravel.CanAdvance,
                selecting ? 0 : journeyRunway.EffectiveCruise, selecting ? 0 : journey.CruiseSpeed,
                selecting ? 0 : journey.TravelMetres, selecting ? 0 : journey.TargetMetres,
                selecting ? 0 : journeyTravel.NoProgressSeconds,
                selecting ? 0 : JourneyRunwayMetres(), selecting ? 0 : journeyPlayback.Current?.Id ?? 0,
                journeyQueue.Count, selecting ? null : journey.Itinerary?.Destination.Name,
                journeyStatus, selecting ? null : journeyLastPreparationFailure);
        }
    }
}
