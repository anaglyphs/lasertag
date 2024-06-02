using Anaglyph.SharedSpaces;
using System;
using Unity.Netcode;

public struct Role : IEquatable<Role>
{
    public NetworkGuid Uuid { get; set; }

    public int TeamNumber { get; set; }
    public bool ReturnToBaseOnDie { get; set; }
    public float BaseRespawnDistance { get; set; }

    public float MaxHealth { get; set; }
    public float GunDamage { get; set; }

    public float HealthRegenerationPerSecond { get; set; }
    public float RespawnTimeSeconds { get; set; }

    // public bool CanShoot { get; set; } // TODO: Need to find a good way to make this work

    public bool Equals(Role other)
    {
        return this.Uuid.guid == other.Uuid.guid; // We might need a better method of equating roles, idk, as long as Netcode is happy :)
    }

    public static Role Default => new Role()
    {
        Uuid = new(Guid.Empty),

        TeamNumber = -1,
        ReturnToBaseOnDie = false,
        BaseRespawnDistance = 1.5f,

        MaxHealth = 100,
        GunDamage = 50,

        HealthRegenerationPerSecond = 10,
        RespawnTimeSeconds = 4,
    };
}