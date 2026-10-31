# Observer requirements and current limits

The final broadcaster should never be a program subject and should have no gameplay interaction or world impact, with invisibility to ordinary players where feasible. The prototype does not yet satisfy full observer isolation.

0.5.0 hides the broadcaster's child renderers **locally** during normal coverage. Original renderer flags restore on stop, and self-follow testing intentionally shows the character. This does not hide the player from other clients or remove its network identity, input, collision, damage, AI targeting, player-count/sleep effects, spawning/raid effects, or simulation ownership. Local render masking is presentation, not spectator mode.

A true observer design must separately verify:

- Local input and gameplay-action suppression while retaining broadcast controls.
- Collision, damage, AI/aggro, and interactions on whichever peer owns simulation.
- Player visibility and replicated effects on unmodified clients.
- Server rules influenced by player presence: counts, sleep, scaling, events and spawning.
- World-interest and simulation ownership when acquiring distant scenes.

Client-only patches cannot simply be assumed to govern server decisions or other peers' simulation. A server companion and potentially changes on simulation-owning clients may be necessary. No universal zero-impact guarantee should be made without verifying those paths against the actual game/network backend. Avoid implementing cosmetic invisibility or invulnerability and labeling it passive observation.

Dynamic idle programming also requires world traversal when local subjects are exhausted. Moving interest alone and teleporting the body have different simulation implications; both need the observer rules above. The solo probe now supplies a way to measure loading behavior independently of other-player availability.
