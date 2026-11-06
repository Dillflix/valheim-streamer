using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

internal sealed partial class ScenicProduction
{
    private JourneyTravelState GuidePhase => guide == null ? JourneyTravelState.Departure :
        journey.Outcome == ScenicJourneyOutcome.Completed ? JourneyTravelState.Completed :
        !journey.Active ? JourneyTravelState.Cancelled : arrivalStarted ? JourneyTravelState.Arriving :
        journey.TravelMetres > .01 ? JourneyTravelState.Travelling : JourneyTravelState.Departure;

    internal string JourneyPictureLabel => JourneyOperatorStatus.DescribePicture(GuidePhase, guide != null,
        false, heldJourney, !heldJourney && watching, guide?.Speed ?? 0, journey.TravelMetres, noAdvance);

    internal JourneyOperatorStatus JourneyOperator => new JourneyOperatorStatus(JourneyOwnsTravel, GuidePhase,
        guide != null, false, heldJourney, watching && !heldJourney, guide?.Speed ?? 0,
        guide == null ? 0 : journey.CruiseSpeed, guide == null ? 0 : journey.RouteProgressMetres,
        guide == null ? 0 : journey.TargetMetres, noAdvance, 0, JourneyGuideId, 0,
        guide == null ? null : journey.Itinerary?.Destination.Name, journeyStatus, null, continuousGuide: true);
}
