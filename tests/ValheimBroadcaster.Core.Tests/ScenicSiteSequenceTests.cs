using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ScenicSiteSequenceTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("NPC scenic identity uses exact known trader tokens", ExactIdentity);
        yield return ("NPC detail uses character scale instead of the structural minimum", CharacterScale);
        yield return ("NPC framing respects narrow aspect and measured actor dimensions", CharacterEnvelope);
        yield return ("Scenic NPC detail requires presented parent context", ContextBeforeActor);
        yield return ("Prepared held and unreadable site shots cannot unlock NPC detail", NoInventedCoverage);
        yield return ("Site sequence prefers unshown NPC then releases emphasis", SubjectProgression);
        yield return ("Site sequence resets and stale visits cannot unlock unrelated NPCs", Lifetime);
        yield return ("Trader destinations require their observed trader and surroundings", TraderDestination);
        yield return ("Generic structures and another trader cannot fulfil a named camp", WrongSubject);
        yield return ("NPC portrait without camp context cannot fulfil destination", PortraitOnly);
        yield return ("Trader context without actor coverage expires honestly", MissingActor);
        yield return ("Construction site progression preserves parent and child context", ConstructionHierarchy);
        yield return ("An interrupted establishing view cannot prematurely advance a site sequence", InterruptedContext);
    }
    private static ScenicFeature Site(string id = "camp", string identity = "$npc_haldor") =>
        new ScenicFeature(id, ScenicFeatureKind.Structure, new AutonomyPosition(0, 10, 0), new AutonomyPosition(0, 0, 1),
            10, 6, .9f, scopeLabel: "Trader surroundings", isPartial: true, subjectKind: ScenicSubjectKind.SiteContext, subjectIdentity: identity);
    private static ScenicFeature Actor(string parent = "camp", string identity = "$npc_haldor") =>
        new ScenicFeature("actor:" + parent, ScenicFeatureKind.Structure, new AutonomyPosition(0, 10, 0), new AutonomyPosition(0, 0, 1),
            1, 2, .95f, parentId: parent, scopeLabel: "Trader", subjectKind: ScenicSubjectKind.Npc, subjectIdentity: identity);
    private static ScenicShotPlan Plan(long id, ScenicFeature f, ScenicShotRole role = ScenicShotRole.Establish) => ScenicShotFactory.Create(id, f, role, 0);
    private static void Assert(bool value) { if (!value) throw new Exception("Assertion failed"); }
    private static void Feed(ScenicDirector director, long id, double from, double seconds, bool readable = true, bool held = false)
    { for (int i = 0; i <= seconds * 10; i++) director.Observe(id, from + i * .1, .1, true, readable, false, held); }
    private static void Coverage(DestinationSubjectCoverage c, ScenicFeature feature, ScenicShotRole role, double from, double seconds)
    { for (int i = 0; i <= seconds * 10; i++) c.Observe(from + i * .1, true, true, false, feature, role); }
    private static WorldInterestSite Destination(string name = "Vendor_BlackForest") =>
        new WorldInterestSite("landmark:trader", "", name, WorldInterestKind.Landmark, new ObserverPoint(0, 10, 0),
            new ObserverPoint(12, 8, 12), new ObserverPoint(20, 12, 0), 0, 50);

    private static void CharacterScale()
    {
        float distance = ScenicNpcComposition.Distance(2, 1, 53, 16f / 9f, true);
        double heightFraction = 2 / (2 * distance * Math.Tan(53 * Math.PI / 360));
        Assert(distance >= 2.5f && distance < 5 && heightFraction >= .50 && heightFraction <= .56);
        Assert(ScenicNpcComposition.Distance(2, 1, 53, 16f / 9f, false) > distance);
        Assert(ScenicNpcComposition.Elevation(2) < .5f);
        var shot = Plan(1, Actor(), ScenicShotRole.Detail);
        Assert(shot.Sample(.5).CameraPosition.Distance(shot.Sample(.5).AimPosition) < 5);
    }
    private static void CharacterEnvelope()
    {
        float wide = ScenicNpcComposition.Distance(2, 1, 53, 16f / 9f, true);
        Assert(ScenicNpcComposition.Distance(2, 1, 53, .5f, true) > wide);
        Assert(ScenicNpcComposition.Distance(4, 1, 53, 16f / 9f, true) > wide);
        bool rejected = false;
        try { ScenicNpcComposition.Distance(float.NaN, 1, 53, 1, true); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Assert(rejected);
    }
    private static void ExactIdentity()
    {
        Assert(ScenicSiteSequence.TryTrader("$npc_haldor", out string label, out string location));
        Assert(label == "Haldor" && location == "Vendor_BlackForest");
        Assert(ScenicSiteSequence.TryTrader("$npc_bogwitch", out _, out _));
        Assert(!ScenicSiteSequence.TryTrader("fake_$npc_haldor", out _, out _));
        Assert(!ScenicSiteSequence.TryTrader("Haldor", out _, out _));
        Assert(!ScenicSiteSequence.TryTrader(null, out _, out _));
    }
    private static void ContextBeforeActor()
    {
        var director = new ScenicDirector(); var detail = Plan(2, Actor(), ScenicShotRole.Detail);
        Assert(!director.CanOffer(detail));
        director.Accept(Plan(1, Site()), 0); Feed(director, 1, 0, 5.9); Assert(!director.CanOffer(detail));
        Feed(director, 1, 6, .2); Assert(director.CanOffer(detail));
        Assert(director.PreferredRole(Actor()) == ScenicShotRole.Detail);
    }
    private static void NoInventedCoverage()
    {
        var director = new ScenicDirector(); director.Stage(Plan(1, Site()));
        Assert(!director.CanOffer(Plan(2, Actor())));
        director.Accept(director.TakePrepared()!, 0);
        Feed(director, 1, 0, 10, held: true); Assert(!director.CanDevelopSubject(Actor()));
        Feed(director, 1, 10.1, 10, readable: false); Assert(!director.CanDevelopSubject(Actor()));
    }
    private static void SubjectProgression()
    {
        var director = new ScenicDirector(); director.Accept(Plan(1, Site()), 0); Feed(director, 1, 0, 6.2);
        Assert(director.SubjectPriority(Actor()) > director.SubjectPriority(Site()));
        director.Accept(Plan(2, Actor(), ScenicShotRole.Detail), 6.3); Feed(director, 2, 6.3, 4.2);
        Assert(director.SubjectPriority(Actor()) < 0);
    }
    private static void Lifetime()
    {
        var sequence = new ScenicSiteSequence();
        for (int i = 0; i < 63; i++) sequence.Observe(i * .1, Site(), ScenicShotRole.Establish, true, false);
        Assert(sequence.CanDevelop(Actor())); Assert(!sequence.CanDevelop(Actor("other")));
        sequence.Observe(400, null, ScenicShotRole.Establish, false, false); Assert(!sequence.CanDevelop(Actor()));
        sequence.Reset(); Assert(!sequence.CanDevelop(Actor()));
    }
    private static void TraderDestination()
    {
        var c = new DestinationSubjectCoverage(); c.Set(Destination());
        Coverage(c, Site(), ScenicShotRole.Establish, 0, 11.1); Assert(!c.Satisfied);
        Assert(c.Priority(Actor()) > c.Priority(Site()));
        Coverage(c, Actor(), ScenicShotRole.Detail, 11.2, 4.2);
        Assert(c.Satisfied && c.NpcSeconds >= 4 && c.EstablishedSeconds >= 6);
    }
    private static void WrongSubject()
    {
        var c = new DestinationSubjectCoverage(); c.Set(Destination());
        var generic = new ScenicFeature("pieces", ScenicFeatureKind.Structure, Site().Center, Site().Direction, 12, 10, .9f);
        Assert(!c.Matches(generic)); Assert(!c.Matches(Site(identity: "$npc_hildir")));
        c.Reset(); c.Set(Destination("Ruins")); Assert(!c.Matches(Site())); Assert(c.Matches(generic));
    }
    private static void PortraitOnly()
    {
        var c = new DestinationSubjectCoverage(); c.Set(Destination());
        Coverage(c, Actor(), ScenicShotRole.Establish, 0, 20);
        Assert(c.ShownSeconds == 0 && c.EstablishedSeconds == 0 && c.NpcSeconds == 0 && !c.Satisfied);
    }
    private static void MissingActor()
    {
        var c = new DestinationSubjectCoverage(); c.Set(Destination());
        Coverage(c, Site(), ScenicShotRole.Establish, 0, 31);
        Assert(c.Unavailable && !c.Satisfied && !c.Prioritize);
    }
    private static void ConstructionHierarchy()
    {
        var sequence = new ScenicSiteSequence();
        var building = new ScenicFeature("building", ScenicFeatureKind.Structure, Site().Center, Site().Direction, 12, 10, .9f,
            parentId: "district", scopeLabel: "Building");
        var room = new ScenicFeature("room", ScenicFeatureKind.Structure, Site().Center, Site().Direction, 3, 4, .9f,
            parentId: "building", scopeLabel: "Room");
        for (int i = 0; i < 63; i++) sequence.Observe(i * .1, building, ScenicShotRole.Establish, true, false);
        Assert(sequence.Priority(room) > 0);
    }

    private static void InterruptedContext()
    {
        var director = new ScenicDirector(); var site = Site();
        director.Accept(Plan(1, site).WithDuration(4), 0); Feed(director, 1, 0, 4);
        Assert(director.RememberedShots == 1 && !director.CanDevelopSubject(Actor()));
        Assert(director.PreferredRole(site) == ScenicShotRole.Establish);
        var second = ScenicShotFactory.Create(2, site, director.PreferredRole(site), 2);
        Assert(director.CanOffer(second));
        director.Accept(second, 4.1); Feed(director, 2, 4.1, 2.2);
        Assert(director.CanDevelopSubject(Actor()) && director.PreferredRole(site) != ScenicShotRole.Establish);
        Assert(director.SubjectPriority(Actor()) > director.SubjectPriority(site));
        director.Reset(); Assert(director.PreferredRole(site) == ScenicShotRole.Establish);
    }
}
