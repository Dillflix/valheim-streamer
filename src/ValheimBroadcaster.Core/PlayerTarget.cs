using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>A player identity; positions and Unity objects belong in the game adapter.</summary>
    public sealed class PlayerTarget
    {
        public PlayerTarget(string id, string name)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("A stable player ID is required.", nameof(id));

            Id = id;
            Name = string.IsNullOrWhiteSpace(name) ? id : name;
        }

        public string Id { get; }
        public string Name { get; }
    }
}
