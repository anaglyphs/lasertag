using System;
using Unity.Netcode;

public class GameManager : SingletonNetworkBehavior<GameManager>
{
    public NetworkList<Role> Roles = new();

    public Role GetRoleByUuid(Guid uuid)
    {
        foreach (Role role in Roles)
        {
            if (role.Uuid.guid == uuid)
                return role;
        }

        return Role.Standard;
    }
}
